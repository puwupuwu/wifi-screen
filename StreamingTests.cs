using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WifiScreen {
    static class StreamingTests {
        static void Check(bool ok,string label){if(!ok)throw new Exception("FAIL: "+label);}
        static async Task<byte[]> Receive(ClientWebSocket ws,CancellationToken cancel){using(var output=new MemoryStream()){var buffer=new byte[8192];WebSocketReceiveResult r;do{r=await ws.ReceiveAsync(new ArraySegment<byte>(buffer),cancel);if(r.MessageType!=WebSocketMessageType.Binary)throw new Exception("Expected binary image");output.Write(buffer,0,r.Count);}while(!r.EndOfMessage);return output.ToArray();}}
        static void Ack(ClientWebSocket ws,CancellationToken cancel){ws.SendAsync(new ArraySegment<byte>(Encoding.ASCII.GetBytes("ok")),WebSocketMessageType.Text,true,cancel).GetAwaiter().GetResult();}
        public static string Run() {
            string path=Path.Combine(Path.GetTempPath(),"WifiScreen-test-"+Guid.NewGuid().ToString("N")+".xml");
            try {
                var settings=new ShareSettings();settings.Save(path);var reloaded=ShareSettings.Load(path);Check(reloaded.Token==settings.Token&&reloaded.Port==8765&&reloaded.Persistent,"persistent settings survive reload");
                reloaded.Port=8766;reloaded.Save(path);Check(ShareSettings.Load(path).Port==8766,"atomic settings update");
                File.WriteAllText(path,"<sharing persistent='True' port='8765' token='../../bad'/>");bool invalid=false;try{ShareSettings.Load(path);}catch(InvalidDataException){invalid=true;}Check(invalid,"reject unsafe saved token");
            }finally{if(File.Exists(path))File.Delete(path);}
            string stableToken=ShareSettings.NewToken(),url;int port;
            using(var server=new ScreenServer(IPAddress.Loopback,0,stableToken)) {
                url=server.Url;port=new Uri(url).Port;
                bool conflict=false;try{using(var other=new ScreenServer(IPAddress.Loopback,port,stableToken)){} }catch(SocketException){conflict=true;}Check(conflict,"fixed port conflict reports failure");
                using(var web=new WebClient()){web.Proxy=null;Check(web.DownloadString(url).Contains("displayed fps"),"viewer FPS meter");}
            }
            using(var server=new ScreenServer(IPAddress.Loopback,port,stableToken)) {
                Check(server.Url==url,"same address after listener restart");
                using(var cancel=new CancellationTokenSource(7000))using(var ws=new ClientWebSocket()) {
                    ws.Options.SetRequestHeader("Origin",new Uri(url).GetLeftPart(UriPartial.Authority));
                    ws.ConnectAsync(new Uri(url.Replace("http://","ws://")+"stream"),cancel.Token).GetAwaiter().GetResult();
                    byte[] first=Enumerable.Range(0,90000).Select(x=>(byte)(x%251)).ToArray();server.SetFrame(first);
                    Check(Receive(ws,cancel.Token).GetAwaiter().GetResult().SequenceEqual(first),"64-bit WebSocket payload length / binary delivery");
                    var pending=Receive(ws,cancel.Token);server.SetFrame(new byte[]{1});server.SetFrame(new byte[]{2,3});Thread.Sleep(100);
                    Check(!pending.IsCompleted,"one in-flight frame until decoder ACK");Ack(ws,cancel.Token);
                    Check(pending.GetAwaiter().GetResult().SequenceEqual(new byte[]{2,3}),"slow viewer receives latest frame, no queue");Ack(ws,cancel.Token);
                    bool marked=SpinWait.SpinUntil(()=>server.RecentViewer("127.0.0.1"),1000);Check(marked,"ACK establishes viewer activity");
                    server.Dispose();
                    bool ended=false;try{Receive(ws,cancel.Token).GetAwaiter().GetResult();}catch(Exception){ended=true;}Check(ended,"stop terminates persistent stream");
                }
            }
            return "PASS: persistent settings/reload, atomic update, invalid-token rejection, fixed-port conflict and exact URL after restart.\r\nPASS: real WebSocket handshake, 90KB frame, decoder ACK, slow-viewer latest-frame delivery, viewer tracking and active-stream shutdown.\r\n";
        }
    }
}
