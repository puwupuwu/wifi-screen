using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace WifiScreen {
    sealed class ShareSettings {
        public bool Persistent=true;
        public int Port=8765;
        public string Token=NewToken();
        public static string DefaultPath { get {return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WifiScreen","sharing.xml");} }
        public static string NewToken(){byte[] bytes=new byte[16];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(bytes);return BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant();}
        public static bool ValidToken(string value){return value!=null && value.Length==32 && System.Text.RegularExpressions.Regex.IsMatch(value,"\\A[0-9a-f]{32}\\z");}
        public static ShareSettings Load(string path) {
            var s=new ShareSettings();if(!File.Exists(path))return s;
            var doc=new XmlDocument();doc.XmlResolver=null;
            using(var reader=XmlReader.Create(path,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null}))doc.Load(reader);
            var root=doc.DocumentElement;
            s.Persistent=Boolean.Parse(root.GetAttribute("persistent"));s.Port=Int32.Parse(root.GetAttribute("port"));s.Token=root.GetAttribute("token");
            if(s.Port<1024||s.Port>65535||!ValidToken(s.Token))throw new InvalidDataException("Invalid saved browser-link settings.");return s;
        }
        public void Save(string path) {
            if(Port<1024||Port>65535||!ValidToken(Token))throw new InvalidDataException("Invalid browser-link settings.");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {
                using(var writer=XmlWriter.Create(temp,new XmlWriterSettings{Indent=true})) {writer.WriteStartElement("sharing");writer.WriteAttributeString("persistent",Persistent.ToString());writer.WriteAttributeString("port",Port.ToString());writer.WriteAttributeString("token",Token);writer.WriteEndElement();}
                if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
            }finally{if(File.Exists(temp))File.Delete(temp);}
        }
    }
    sealed class ScreenServer : IDisposable {
        readonly TcpListener listener;readonly string token;readonly object gate=new object();
        readonly HashSet<TcpClient> clients=new HashSet<TcpClient>();readonly Dictionary<string,DateTime> viewers=new Dictionary<string,DateTime>();
        volatile bool running=true;byte[] frame;long version,framesSent;
        public string Url{get;private set;}
        public long FramesSent{get{return Interlocked.Read(ref framesSent);}}
        public bool RecentViewer(string address){lock(gate){DateTime t;return viewers.TryGetValue(address,out t)&&(DateTime.UtcNow-t).TotalSeconds<5;}}
        public ScreenServer(IPAddress address):this(address,0,ShareSettings.NewToken()){}
        public ScreenServer(IPAddress address,int port,string token) {
            if(!ShareSettings.ValidToken(token))throw new ArgumentException("Invalid session token");this.token=token;
            listener=new TcpListener(address,port);listener.Server.ExclusiveAddressUse=true;
            try{listener.Start(16);}catch{listener.Stop();throw;}
            Url="http://"+address+":"+((IPEndPoint)listener.LocalEndpoint).Port+"/"+token+"/";
            Task.Run((Action)Accept);
        }
        public void SetFrame(byte[] bytes){lock(gate){if(!running)return;frame=bytes;version++;Monitor.PulseAll(gate);}}
        void Accept(){while(running)try{var client=listener.AcceptTcpClient();lock(gate){if(!running||clients.Count>=8){client.Close();continue;}clients.Add(client);}Task.Run(delegate{try{Handle(client);}catch(IOException){}catch(SocketException){}catch(ObjectDisposedException){}catch(InvalidDataException){}finally{lock(gate)clients.Remove(client);client.Close();}});}catch(SocketException){if(!running)break;}catch(ObjectDisposedException){break;}}
        void Mark(TcpClient client){Interlocked.Increment(ref framesSent);lock(gate){if(running)viewers[((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString()]=DateTime.UtcNow;}}
        void Handle(TcpClient client) {
            client.NoDelay=true;client.ReceiveTimeout=3000;client.SendTimeout=3000;
            using(var stream=client.GetStream()) {
                var header=new List<byte>();bool complete=false;
                while(header.Count<8192){int c=stream.ReadByte();if(c<0)return;header.Add((byte)c);int n=header.Count;if(n>=4&&header[n-4]==13&&header[n-3]==10&&header[n-2]==13&&header[n-1]==10){complete=true;break;}}
                if(!complete){Reply(stream,"431 Request Header Fields Too Large","text/plain",new byte[0]);return;}
                var lines=Encoding.ASCII.GetString(header.ToArray()).Split(new[]{"\r\n"},StringSplitOptions.None);var first=lines[0].Split(' ');
                if(first.Length!=3||first[0]!="GET"){Reply(stream,"405 Method Not Allowed","text/plain",new byte[0]);return;}
                string path=first[1].Split('?')[0],root="/"+token+"/";
                if(!running)return;
                if(path==root||path==root.TrimEnd('/')){Reply(stream,"200 OK","text/html; charset=utf-8",Encoding.UTF8.GetBytes(Viewer(root)));return;}
                if(path==root+"frame.jpg") {byte[] bytes;lock(gate)bytes=frame;if(bytes==null){Reply(stream,"503 Service Unavailable","text/plain",new byte[0]);return;}Reply(stream,"200 OK","image/jpeg",bytes);Mark(client);return;}
                if(path==root+"stream") {
                    var fields=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                    foreach(string line in lines){int i=line.IndexOf(':');if(i>0)fields[line.Substring(0,i).Trim()]=line.Substring(i+1).Trim();}
                    string key,upgrade,connection,wsVersion,origin;
                    if(!fields.TryGetValue("Sec-WebSocket-Key",out key)||!fields.TryGetValue("Upgrade",out upgrade)||!upgrade.Equals("websocket",StringComparison.OrdinalIgnoreCase)||!fields.TryGetValue("Connection",out connection)||connection.IndexOf("upgrade",StringComparison.OrdinalIgnoreCase)<0||!fields.TryGetValue("Sec-WebSocket-Version",out wsVersion)||wsVersion!="13") {Reply(stream,"400 Bad Request","text/plain",new byte[0]);return;}
                    try{if(Convert.FromBase64String(key).Length!=16)throw new FormatException();}catch(FormatException){Reply(stream,"400 Bad Request","text/plain",new byte[0]);return;}
                    if(fields.TryGetValue("Origin",out origin)&&origin!=new Uri(Url).GetLeftPart(UriPartial.Authority)){Reply(stream,"403 Forbidden","text/plain",new byte[0]);return;}
                    string accept;using(var sha=SHA1.Create())accept=Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(key+"258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                    Write(stream,Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: "+accept+"\r\n\r\n"));
                    long sent=-1;client.ReceiveTimeout=5000;
                    while(running) {
                        byte[] bytes;lock(gate){while(running&&(frame==null||version==sent))Monitor.Wait(gate,1000);if(!running)return;bytes=frame;sent=version;}
                        SendWs(stream,2,bytes);
                        // One in-flight image. The TV acknowledges after decoding/drawing;
                        // slow viewers skip old frames rather than building latency.
                        if(!ReadAck(stream))return;Mark(client);
                    }
                    return;
                }
                Reply(stream,"404 Not Found","text/plain",Encoding.UTF8.GetBytes("Session not found"));
            }
        }
        static void Write(Stream stream,byte[] bytes){stream.Write(bytes,0,bytes.Length);}
        static byte[] ReadExact(Stream stream,int count){var bytes=new byte[count];int n=0;while(n<count){int got=stream.Read(bytes,n,count-n);if(got==0)throw new EndOfStreamException();n+=got;}return bytes;}
        static bool ReadAck(Stream stream) {
            for(int attempts=0;attempts<5;attempts++) {
                byte[] h=ReadExact(stream,2);int opcode=h[0]&15,len=h[1]&127;
                if((h[0]&240)!=128||(h[1]&128)==0||len>125)throw new InvalidDataException("Invalid WebSocket control frame");
                byte[] mask=ReadExact(stream,4),payload=ReadExact(stream,len);for(int i=0;i<len;i++)payload[i]^=mask[i%4];
                if(opcode==8){SendWs(stream,8,payload);return false;}if(opcode==9){SendWs(stream,10,payload);continue;}if(opcode==10)continue;
                return opcode==1 && Encoding.ASCII.GetString(payload)=="ok";
            }return false;
        }
        static void SendWs(Stream stream,int opcode,byte[] bytes) {
            var header=new List<byte>();header.Add((byte)(128|opcode));
            if(bytes.Length<126)header.Add((byte)bytes.Length);
            else if(bytes.Length<=65535){header.Add(126);header.Add((byte)(bytes.Length>>8));header.Add((byte)bytes.Length);}
            else{header.Add(127);for(int shift=56;shift>=0;shift-=8)header.Add((byte)((ulong)bytes.Length>>shift));}
            Write(stream,header.ToArray());Write(stream,bytes);
        }
        static void Reply(Stream stream,string status,string type,byte[] body){Write(stream,Encoding.ASCII.GetBytes("HTTP/1.1 "+status+"\r\nContent-Type: "+type+"\r\nContent-Length: "+body.Length+"\r\nCache-Control: no-store\r\nReferrer-Policy: no-referrer\r\nX-Content-Type-Options: nosniff\r\nX-Frame-Options: DENY\r\nConnection: close\r\n\r\n"));Write(stream,body);}
        public static string Viewer(string root) {
            return @"<!doctype html><html><head><meta name='viewport' content='width=device-width,initial-scale=1'><title>WiFi Screen</title><style>html,body{margin:0;height:100%;overflow:hidden;background:#080c13;color:white;font:16px Arial}canvas{width:100%;height:100%;object-fit:contain}#bar{position:fixed;top:10px;left:10px;background:#172033dd;padding:12px;border-radius:8px}button{padding:8px;margin-left:10px}</style></head><body><canvas id='screen'></canvas><div id='bar'><span id='state'>Connecting...</span><button id='full'>Full screen</button><button id='compat'>Compatibility mode</button></div><script>
var root='"+root+@"',canvas=document.getElementById('screen'),ctx=canvas.getContext('2d'),state=document.getElementById('state'),ws=null,mode='stream',last=0,count=0,epoch=Date.now(),fps=0,retry=null;
function status(s){state.textContent=s;}
function draw(img){if(canvas.width!==img.width||canvas.height!==img.height){canvas.width=img.width;canvas.height=img.height;}ctx.drawImage(img,0,0);last=Date.now();count++;if(last-epoch>=1000){fps=count*1000/(last-epoch);epoch=last;count=0;}status(fps.toFixed(1)+' displayed fps · '+mode+' · no audio');}
function clear(){ctx.clearRect(0,0,canvas.width,canvas.height);status('Disconnected — waiting for sharing to start on PC');}
function connect(){if(mode!=='stream')return;try{ws=new WebSocket('ws://'+location.host+root+'stream');ws.binaryType='blob';ws.onmessage=function(e){var socket=this,img=new Image(),u=URL.createObjectURL(e.data);img.onload=function(){if(socket===ws&&socket.readyState===1){draw(img);socket.send('ok');}URL.revokeObjectURL(u);};img.onerror=function(){URL.revokeObjectURL(u);socket.close();};img.src=u;};ws.onclose=function(){if(this!==ws)return;clear();if(mode==='stream')retry=setTimeout(connect,1000);};ws.onerror=function(){this.close();};}catch(e){compat();}}
function poll(){if(mode!=='compatibility')return;var img=new Image(),begin=Date.now();img.onload=function(){if(mode!=='compatibility')return;draw(img);retry=setTimeout(poll,Math.max(0,33-(Date.now()-begin)));};img.onerror=function(){clear();retry=setTimeout(poll,1000);};img.src=root+'frame.jpg?t='+Date.now();}
function compat(){if(mode==='compatibility')return;mode='compatibility';clearTimeout(retry);if(ws){var old=ws;ws=null;old.close();}poll();}
document.getElementById('compat').onclick=compat;
document.getElementById('full').onclick=function(){var e=document.documentElement;if(e.requestFullscreen)e.requestFullscreen();else if(e.webkitRequestFullscreen)e.webkitRequestFullscreen();};
setInterval(function(){if(last&&Date.now()-last>3000)clear();},1000);
if(window.WebSocket&&window.URL&&window.URL.createObjectURL)connect();else compat();
</script></body></html>";
        }
        public void Dispose(){running=false;listener.Stop();lock(gate){frame=null;viewers.Clear();Monitor.PulseAll(gate);foreach(var c in clients)c.Close();}}
    }
    sealed class CapturePump : IDisposable {
        [System.Runtime.InteropServices.DllImport("winmm.dll")]static extern uint timeBeginPeriod(uint period);
        [System.Runtime.InteropServices.DllImport("winmm.dll")]static extern uint timeEndPeriod(uint period);
        readonly ManualResetEvent stop=new ManualResetEvent(false);readonly Thread thread;readonly ScreenServer server;readonly Rectangle bounds;readonly int fps,width,quality;
        public volatile string Error;public double MeasuredFps {get{return BitConverter.Int64BitsToDouble(Interlocked.Read(ref measured));}}long measured;
        public CapturePump(Rectangle bounds,ScreenServer server,int fps,int width,int quality){this.bounds=bounds;this.server=server;this.fps=fps;this.width=width;this.quality=quality;thread=new Thread(Run){IsBackground=true,Name="WiFi Screen capture"};thread.Start();}
        void Run(){bool precise=timeBeginPeriod(1)==0;try{using(var capture=new CaptureEngine(bounds,width,quality)){var clock=Stopwatch.StartNew();double next=0,epoch=0;int count=0;while(!stop.WaitOne(0)){server.SetFrame(capture.Frame());count++;double now=clock.Elapsed.TotalMilliseconds;if(now-epoch>=1000){Interlocked.Exchange(ref measured,BitConverter.DoubleToInt64Bits(count*1000.0/(now-epoch)));count=0;epoch=now;}next+=1000.0/fps;if(next<now)next=now;stop.WaitOne(Math.Max(0,(int)(next-clock.Elapsed.TotalMilliseconds)));}}}catch(Exception e){Error=e.Message;}finally{if(precise)timeEndPeriod(1);}}
        public void Dispose(){stop.Set();thread.Join();stop.Dispose();}
    }
}
