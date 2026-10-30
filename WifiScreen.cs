using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("WiFi Screen")]
[assembly: System.Reflection.AssemblyVersion("1.4.0.0")]

namespace WifiScreen {
    static class Program {
        [STAThread] static int Main(string[] args) {
            if (args.Length > 0 && args[0] == "--self-test") {
                try { string report = Tests.Run(); File.WriteAllText(args.Length > 1 ? args[1] : "self-test.txt", report); return 0; }
                catch (Exception e) { if (args.Length > 1) File.WriteAllText(args[1], e.ToString()); return 1; }
            }
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            var form = new MainForm(args.Length==2?args[1]+".settings.xml":null);
            if (args.Length == 2 && (args[0] == "--ui-smoke" || args[0] == "--layout-test")) {
                int result = 0;
                form.Shown += delegate {
                    form.BeginInvoke((Action)delegate {
                        try { if(args[0] == "--layout-test") form.LayoutTest(args[1]); else form.SmokeTest(args[1]); }
                        catch(Exception e) { File.WriteAllText(args[1] + ".txt", e.ToString()); result = 1; }
                        finally { form.Close(); }
                    });
                };
                Application.Run(form); return result;
            }
            Application.Run(form); return 0;
        }
    }

    sealed class Adapter {
        public IPAddress Address; public string Name; public int Rank;
        public override string ToString() { return Name + "  ·  " + Address; }
        public static List<Adapter> All() {
            var result = new List<Adapter>();
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()) {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback || nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                foreach (var a in nic.GetIPProperties().UnicastAddresses)
                    if (a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address) && !(a.Address.GetAddressBytes()[0] == 169 && a.Address.GetAddressBytes()[1] == 254))
                        result.Add(new Adapter { Address = a.Address, Name = nic.Name, Rank = (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 0 : 10) + (nic.GetIPProperties().GatewayAddresses.Count > 0 ? 0 : 20) });
            }
            return result.GroupBy(a => a.Address.ToString()).Select(g => g.First()).OrderBy(a => a.Rank).ToList();
        }
    }
    sealed class Device {
        public string Name, Protocol, Address, Detail;
        public string Key { get { return Protocol + "|" + Address + "|" + Name; } }
    }
    sealed class DnsRecord {
        public string Name, Target, Text; public int Type;
    }
    static class Dns {
        static int U16(byte[] b, ref int p) { if (p + 2 > b.Length) throw new InvalidDataException(); int v = b[p] * 256 + b[p+1]; p += 2; return v; }
        public static string Name(byte[] b, ref int p) {
            var labels = new List<string>(); int at = p, resume = -1, steps = 0, total = 0;
            while (true) {
                if (at >= b.Length || ++steps > 128) throw new InvalidDataException("Invalid DNS name");
                int n = b[at++];
                if (n == 0) break;
                if ((n & 192) == 192) {
                    if (at >= b.Length) throw new InvalidDataException();
                    if (resume < 0) resume = at + 1;
                    at = ((n & 63) << 8) | b[at]; continue;
                }
                if (n > 63 || at + n > b.Length || (total += n + 1) > 255) throw new InvalidDataException();
                labels.Add(Encoding.UTF8.GetString(b, at, n)); at += n;
            }
            p = resume < 0 ? at : resume; return String.Join(".", labels);
        }
        public static List<DnsRecord> Parse(byte[] b) {
            if (b.Length < 12) throw new InvalidDataException();
            int p = 4, questions = U16(b, ref p), count = U16(b, ref p) + U16(b, ref p) + U16(b, ref p);
            if (questions > 128 || count > 512) throw new InvalidDataException();
            for (int i = 0; i < questions; i++) { Name(b, ref p); U16(b, ref p); U16(b, ref p); }
            var list = new List<DnsRecord>();
            for (int i = 0; i < count; i++) {
                var r = new DnsRecord { Name = Name(b, ref p) }; r.Type = U16(b, ref p); U16(b, ref p);
                if (p + 4 > b.Length) throw new InvalidDataException();
                bool expired = b[p] == 0 && b[p+1] == 0 && b[p+2] == 0 && b[p+3] == 0;
                p += 4; int len = U16(b, ref p), end = p + len;
                if (end > b.Length) throw new InvalidDataException();
                if (r.Type == 12) { int q = p; r.Target = Name(b, ref q); if (q > end) throw new InvalidDataException(); }
                else if (r.Type == 16) {
                    var values = new List<string>(); int q = p;
                    while (q < end) { int n = b[q++]; if (q+n > end) throw new InvalidDataException(); values.Add(Encoding.UTF8.GetString(b,q,n)); q += n; }
                    r.Text = String.Join("\n", values);
                }
                p = end; if (!expired) list.Add(r);
            }
            return list;
        }
        public static byte[] Query(string service) {
            var b = new List<byte>(new byte[] {0,0,0,0,0,1,0,0,0,0,0,0});
            foreach (string s in service.Split('.')) { byte[] v = Encoding.UTF8.GetBytes(s); b.Add((byte)v.Length); b.AddRange(v); }
            b.AddRange(new byte[] {0,0,12,128,1}); // PTR, request unicast reply
            return b.ToArray();
        }
        public static List<Device> Devices(IEnumerable<DnsRecord> records, string address) {
            var all = records.ToList(); var result = new List<Device>();
            foreach (var r in all) {
                string instance = r.Type == 12 ? r.Target : r.Name;
                if (String.IsNullOrEmpty(instance)) continue;
                string protocol = null, suffix = null;
                if (instance.EndsWith("._airplay._tcp.local", StringComparison.OrdinalIgnoreCase)) { protocol = "AirPlay"; suffix = "._airplay._tcp.local"; }
                if (instance.EndsWith("._googlecast._tcp.local", StringComparison.OrdinalIgnoreCase)) { protocol = "Google Cast"; suffix = "._googlecast._tcp.local"; }
                if (protocol == null) continue;
                string name = instance.Substring(0, instance.Length - suffix.Length);
                var txt = all.FirstOrDefault(x => x.Type == 16 && x.Name.Equals(instance, StringComparison.OrdinalIgnoreCase));
                if (txt != null && txt.Text != null) foreach (string entry in txt.Text.Split('\n'))
                    if (entry.StartsWith("fn=", StringComparison.OrdinalIgnoreCase) && entry.Length > 3) name = entry.Substring(3);
                result.Add(new Device { Name = name, Protocol = protocol, Address = address, Detail = protocol == "AirPlay" ? "Open the AirPlay tab for the bundled AirSpan sender" : "Screen casting through Google Chrome" });
            }
            return result.GroupBy(x => x.Key).Select(g => g.First()).ToList();
        }
    }
    static class Discovery {
        public static Device ParseSsdp(string response, string ip) {
            if (!response.StartsWith("HTTP/1.1 200", StringComparison.OrdinalIgnoreCase)) return null;
            var headers = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in response.Split('\n')) { int i = line.IndexOf(':'); if (i > 0) headers[line.Substring(0,i).Trim()] = line.Substring(i+1).Trim(); }
            string st; if (!headers.TryGetValue("ST", out st)) return null;
            string protocol = st.IndexOf(":MediaRenderer:", StringComparison.OrdinalIgnoreCase) >= 0 ? "DLNA" : st.IndexOf(":dial:", StringComparison.OrdinalIgnoreCase) >= 0 ? "DIAL" : null;
            if (protocol == null) return null;
            string server; headers.TryGetValue("SERVER", out server);
            return new Device { Name = protocol + " receiver · " + ip, Address = ip, Protocol = protocol, Detail = "Discovery only" + (server == null ? "" : " · " + server) };
        }
        public static Task Scan(Action<Device> found, Action<string> status, CancellationToken cancel) {
            return Task.Run(delegate {
                var jobs = new List<Task>();
                foreach (var a in Adapter.All()) {
                    Adapter adapter = a;
                    jobs.Add(Task.Run(() => Mdns(adapter, found, status, cancel)));
                    jobs.Add(Task.Run(() => Ssdp(adapter, found, status, cancel)));
                }
                if (jobs.Count == 0) status("No active IPv4 network adapters. Connect to Wi-Fi and scan again.");
                Task.WaitAll(jobs.ToArray());
            });
        }
        static void Mdns(Adapter adapter, Action<Device> found, Action<string> status, CancellationToken cancel) {
            try {
                using (var udp = new UdpClient(AddressFamily.InterNetwork)) {
                    udp.Client.Bind(new IPEndPoint(adapter.Address, 0)); udp.Client.ReceiveTimeout = 350;
                    udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, adapter.Address.GetAddressBytes());
                    var end = DateTime.UtcNow.AddSeconds(5); var next = DateTime.MinValue;
                    while (DateTime.UtcNow < end && !cancel.IsCancellationRequested) {
                        if (DateTime.UtcNow > next) {
                            foreach (string service in new[] { "_airplay._tcp.local", "_googlecast._tcp.local" }) {
                                byte[] q = Dns.Query(service); udp.Send(q,q.Length,new IPEndPoint(IPAddress.Parse("224.0.0.251"),5353));
                            }
                            next = DateTime.UtcNow.AddSeconds(1.5);
                        }
                        try {
                            IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0); byte[] b = udp.Receive(ref remote);
                            if (remote.Port != 5353) continue;
                            foreach (var d in Dns.Devices(Dns.Parse(b),remote.Address.ToString())) found(d);
                        } catch (SocketException e) { if (e.SocketErrorCode != SocketError.TimedOut) throw; }
                          catch (InvalidDataException) { }
                    }
                }
            } catch (Exception e) { status("mDNS on " + adapter.Name + ": " + e.Message); }
        }
        static void Ssdp(Adapter adapter, Action<Device> found, Action<string> status, CancellationToken cancel) {
            try {
                using (var udp = new UdpClient(new IPEndPoint(adapter.Address,0))) {
                    udp.Client.ReceiveTimeout = 350;
                    udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, adapter.Address.GetAddressBytes());
                    var end = DateTime.UtcNow.AddSeconds(5); var next = DateTime.MinValue;
                    while (DateTime.UtcNow < end && !cancel.IsCancellationRequested) {
                        if (DateTime.UtcNow > next) {
                            foreach (string target in new[] { "urn:schemas-upnp-org:device:MediaRenderer:1", "urn:dial-multiscreen-org:service:dial:1" }) {
                                byte[] q = Encoding.ASCII.GetBytes("M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\nST: " + target + "\r\n\r\n");
                                udp.Send(q,q.Length,new IPEndPoint(IPAddress.Parse("239.255.255.250"),1900));
                            }
                            next = DateTime.UtcNow.AddSeconds(2);
                        }
                        try {
                            IPEndPoint remote = new IPEndPoint(IPAddress.Any,0); var b = udp.Receive(ref remote);
                            var d = ParseSsdp(Encoding.UTF8.GetString(b),remote.Address.ToString()); if (d != null) found(d);
                        } catch (SocketException e) { if (e.SocketErrorCode != SocketError.TimedOut) throw; }
                    }
                }
            } catch (Exception e) { status("SSDP on " + adapter.Name + ": " + e.Message); }
        }
    }

    static class Tests {
        static void Check(bool value,string message) { if(!value) throw new Exception("FAIL: " + message); }
        public static string Run() {
            var log=new StringBuilder();
            int p=0; Check(Dns.Name(new byte[] {3,116,118,49,0},ref p)=="tv1" && p==5,"DNS label");
            p=5; Check(Dns.Name(new byte[] {3,116,118,49,0,192,0},ref p)=="tv1" && p==7,"DNS compression");
            bool rejected=false; try { p=0; Dns.Name(new byte[] {192,0},ref p); } catch(InvalidDataException) { rejected=true; } Check(rejected,"DNS pointer loop");
            var query=Dns.Query("_airplay._tcp.local"); Check(Dns.Parse(query).Count==0,"DNS query encoding");
            string instance="Living Room._googlecast._tcp.local";
            var records=new[] {new DnsRecord {Name="_googlecast._tcp.local",Type=12,Target=instance},new DnsRecord {Name=instance,Type=16,Text="fn=Living room TV\nmd=Chromecast"}};
            var found=Dns.Devices(records,"192.168.1.5"); Check(found.Count==1 && found[0].Name=="Living room TV" && found[0].Protocol=="Google Cast","Cast discovery");
            Check(Dns.Devices(new[] {new DnsRecord {Type=12,Target="TV._airplay._tcp.local"}},"192.168.1.6").Single().Protocol=="AirPlay","AirPlay discovery");
            Check(Dns.Devices(new[] {new DnsRecord {Type=12,Target="Speaker._raop._tcp.local"}},"192.168.1.6").Count==0,"Audio receiver exclusion");
            log.AppendLine("PASS: DNS names, compression, malformed pointer rejection, query, AirPlay/Cast classification and RAOP exclusion.");
            Check(Discovery.ParseSsdp("HTTP/1.1 200 OK\r\nST: urn:schemas-upnp-org:device:MediaRenderer:1\r\nSERVER: TV\r\n\r\n","192.168.1.7").Protocol=="DLNA","SSDP DLNA");
            Check(Discovery.ParseSsdp("HTTP/1.1 200 OK\r\nST: urn:dial-multiscreen-org:service:dial:1\r\n\r\n","192.168.1.7").Protocol=="DIAL","SSDP DIAL");
            Check(Discovery.ParseSsdp("HTTP/1.1 200 OK\r\nST: upnp:rootdevice\r\n\r\n","192.168.1.1")==null,"Ignore non-cast UPnP");
            log.AppendLine("PASS: SSDP DLNA/DIAL parsing and unrelated-device exclusion.");
            string oldUrl;
            using(var server=new ScreenServer(IPAddress.Loopback)) {
                oldUrl=server.Url;
                using(var web=new WebClient()) {
                    Check(web.DownloadString(server.Url).Contains("frame.jpg"),"Browser viewer");
                    bool missing=false; try { web.DownloadData(server.Url+"frame.jpg"); } catch(WebException e) { using(var r=(HttpWebResponse)e.Response) missing=r.StatusCode==HttpStatusCode.ServiceUnavailable; } Check(missing,"No frame before capture");
                    byte[] jpeg; using(var bmp=new Bitmap(32,32)) using(var ms=new MemoryStream()) { bmp.Save(ms,ImageFormat.Jpeg); jpeg=ms.ToArray(); }
                    server.SetFrame(jpeg); var received=web.DownloadData(server.Url+"frame.jpg?t=1"); Check(received.SequenceEqual(jpeg),"JPEG delivery");
                    using(var ms=new MemoryStream(received)) using(var image=Image.FromStream(ms)) Check(image.Width==32,"JPEG decodable");
                    bool denied=false; try { web.DownloadData(new Uri(new Uri(server.Url),"/wrong/frame.jpg")); } catch(WebException e) { using(var r=(HttpWebResponse)e.Response) denied=r.StatusCode==HttpStatusCode.NotFound; } Check(denied,"Secret URL enforced");
                }
            }
            using(var web=new WebClient()) { bool closed=false; try { web.DownloadString(oldUrl); } catch(WebException) { closed=true; } Check(closed,"Stop closes listener"); }
            using(var fresh=new ScreenServer(IPAddress.Loopback)) Check(new Uri(fresh.Url).AbsolutePath!=new Uri(oldUrl).AbsolutePath,"Session secret rotation");
            log.AppendLine("PASS: Real HTTP viewer, no-frame response, JPEG stream, unauthorized URL, stop and session rotation.");
            var random=new Random(42);
            for(int i=0;i<2000;i++) { byte[] bytes=new byte[random.Next(0,256)]; random.NextBytes(bytes); try { Dns.Parse(bytes); } catch(InvalidDataException) { } }
            log.AppendLine("PASS: 2,000 malformed DNS packets handled without unexpected exceptions.");
            log.Append(StreamingTests.Run());
            log.AppendLine("Hardware casting / physical TV interoperability: NOT TESTED.");
            return log.ToString();
        }
    }
}
