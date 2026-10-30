using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WifiScreen {
    sealed class MainForm : Form {
        readonly Color background=Color.FromArgb(15,21,32), surface=Color.FromArgb(24,33,48), muted=Color.FromArgb(180,193,211);
        readonly float uiScale;
        TabControl tabs;
        TextBox target, url, diagnostics;
        ComboBox screens, adapters;
        Label scanStatus, routeStatus, shareStatus, receiverDetail;
        ListView devices;
        Button scan, check, start, stop, copy, preview;
        CheckBox persistent;
        NumericUpDown port;
        ComboBox frameRate, resolution;
        Button resetLink;
        ShareSettings settings;
        readonly string settingsPath;
        readonly Dictionary<string,ListViewItem> rows=new Dictionary<string,ListViewItem>();
        readonly List<string> scanErrors=new List<string>();
        readonly List<TableLayoutPanel> contentTables=new List<TableLayoutPanel>();
        CancellationTokenSource scanCancel;
        ScreenServer server;
        CapturePump capture;
        DateTime started;
        readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
        [DllImport("user32.dll")] static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
        int Px(int n) { return (int)Math.Round(n*uiScale); }

        public MainForm(string settingsFile=null) {
            settingsPath=settingsFile??ShareSettings.DefaultPath;
            using(var graphics=CreateGraphics()) uiScale=graphics.DpiX/96F;
            AutoScaleMode=AutoScaleMode.None;
            Font=new Font("Segoe UI",10F);
            Text="WiFi Screen 1.4"; BackColor=background; ForeColor=Color.White;
            try{settings=ShareSettings.Load(settingsPath);}catch(Exception e){settings=new ShareSettings();MessageBox.Show("Unable to load the saved sharing link. A new link will be used.\n"+e.Message,"WiFi Screen");}
            StartPosition=FormStartPosition.CenterScreen;
            var area=Screen.FromPoint(Cursor.Position).WorkingArea;
            Size=new Size(Math.Min(Px(960),area.Width-Px(24)),Math.Min(Px(760),area.Height-Px(24)));
            MinimumSize=new Size(Math.Min(Px(560),area.Width),Math.Min(Px(420),area.Height));
            var root=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Padding=new Padding(Px(18)) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            var header=new Label { Text="WiFi Screen",Font=new Font("Segoe UI",22,FontStyle.Bold),AutoSize=true,Margin=new Padding(0,0,0,Px(12)) };
            root.Controls.Add(header,0,0);
            tabs=new TabControl { Dock=DockStyle.Fill,Padding=new Point(Px(12),Px(8)),Margin=Padding.Empty,Multiline=true };
            root.Controls.Add(tabs,0,1); Controls.Add(root);

            var connect=Page("Connect TV");
            Add(connect,Heading("Connect to your TCL TV"));
            Add(connect,TextLine("Choose Google Cast for a TV that advertises it. Windows Cast is a separate option for Miracast receivers."));
            Add(connect,FieldLabel("TV address"));
            target=new TextBox { Text="192.168.100.10",Dock=DockStyle.Top,Margin=new Padding(0,0,0,Px(8)) }; Add(connect,target);
            var targetActions=Actions();
            check=Button("Check TV connection",async delegate { await CheckTv(); },true); targetActions.Controls.Add(check);
            targetActions.Controls.Add(Button("Google Cast setup",delegate { tabs.SelectedIndex=1; },false));
            targetActions.Controls.Add(Button("AirPlay sender",delegate { tabs.SelectedIndex=4; },false));
            targetActions.Controls.Add(Button("Open Windows Cast",delegate { OpenCast(); },false)); Add(connect,targetActions);
            routeStatus=TextLine("Check the TV to verify its casting ports and choose the right Wi-Fi adapter."); Add(connect,routeStatus);
            Add(connect,Heading("Discovered receiver services"));
            var scanning=Actions(); scan=Button("Scan network",async delegate { await Scan(); },false); scanning.Controls.Add(scan); Add(connect,scanning);
            scanStatus=TextLine("AirPlay · Google Cast · DLNA · DIAL. Miracast devices appear in Windows Cast."); Add(connect,scanStatus);
            devices=new ListView { View=View.Details,FullRowSelect=true,MultiSelect=false,BackColor=surface,ForeColor=Color.White,BorderStyle=BorderStyle.FixedSingle,HideSelection=false,Dock=DockStyle.Top,Height=Px(180),Margin=new Padding(0,0,0,Px(10)),AccessibleName="Discovered receivers" };
            devices.Columns.Add("Receiver",Px(200)); devices.Columns.Add("Protocol",Px(110)); devices.Columns.Add("IP address",Px(150));
            devices.SizeChanged+=delegate { if(devices.ClientSize.Width>0) { devices.Columns[1].Width=Px(105); devices.Columns[2].Width=Px(140); devices.Columns[0].Width=Math.Max(Px(100),devices.ClientSize.Width-Px(250)); } };
            devices.SelectedIndexChanged+=delegate { if(devices.SelectedItems.Count>0) { var d=(Device)devices.SelectedItems[0].Tag; receiverDetail.Text=d.Name+" · "+d.Protocol+"\n"+d.Detail; if(server==null) {target.Text=d.Address;SelectRoute();} } };
            devices.DoubleClick+=delegate { if(devices.SelectedItems.Count>0) {var d=(Device)devices.SelectedItems[0].Tag;if(d.Protocol=="Google Cast")tabs.SelectedIndex=1;else if(d.Protocol=="AirPlay")tabs.SelectedIndex=4;} };
            Add(connect,devices);
            receiverDetail=TextLine("Select a receiver to see what it supports. Discovery alone does not start screen sharing."); Add(connect,receiverDetail);

            var cast=Page("Google Cast");
            Add(cast,Heading("Share your entire desktop with Chrome"));
            Add(cast,TextLine("Your TCL TV advertised Google Cast in the first scan. This is the recommended path for that service; it does not require a browser on the TV."));
            Add(cast,TextLine("1. Keep the PC and TV on the same Wi-Fi network.\n\n2. Open Google Chrome on the PC.\n\n3. Open the three-dot menu, then Cast, save and share > Cast.\n\n4. In the Cast panel, choose Sources > Cast screen.\n\n5. Select TV (your TCL at 192.168.100.10), then choose the screen to share.\n\nStop the session in Chrome's Cast control."));
            var browserActions=Actions(); browserActions.Controls.Add(Button("Open Chrome",delegate { OpenChrome(); },true)); browserActions.Controls.Add(Button("TCL casting instructions",delegate { OpenUrl("https://support.tcl.com/en_CA/screen-mirroring-to-a-tcl-google-tv"); },false)); Add(cast,browserActions);
            Add(cast,Heading("If the TV does not appear"));
            Add(cast,TextLine("Return to Connect TV and run Check TV connection and Scan network. A successful port check shows network reachability, not a completed casting session.\n\nKeep the TV awake. Guest Wi-Fi, client isolation and VPN routing can interfere. Allow the browser on your private network if Windows requests it.\n\nWindows Cast (Win+K) requires Miracast support. A Google Cast or AirPlay advertisement does not establish Miracast support."));
            Add(cast,TextLine("WiFi Screen discovers Google Cast receivers and guides the Chrome connection. It does not contain a native Google Cast or AirPlay sender."));

            var browser=Page("Browser sharing");
            Add(browser,Heading("Share to a browser running on the TV"));
            Add(browser,TextLine("This option needs an actual web browser on the TV. Pressing Start does not launch a receiver app or connect to AirPlay / Chromecast. For Google Cast, use the Google Cast tab."));
            Add(browser,FieldLabel("Screen to share")); screens=new ComboBox { Dock=DockStyle.Top,DropDownStyle=ComboBoxStyle.DropDownList,Margin=new Padding(0,0,0,Px(10)),AccessibleName="Screen to share" };
            RefreshScreens(); Add(browser,screens);
            Add(browser,FieldLabel("PC network address — selected using the route to your TV"));
            adapters=new ComboBox { Dock=DockStyle.Top,DropDownStyle=ComboBoxStyle.DropDownList,Margin=new Padding(0,0,0,Px(10)),AccessibleName="Network adapter" }; Add(browser,adapters); RefreshAdapters();
            persistent=new CheckBox{Text="Keep the same browser link between sessions",Checked=settings.Persistent,AutoSize=true,Margin=new Padding(0,0,0,Px(10))};Add(browser,persistent);
            Add(browser,FieldLabel("Fixed port (used only when keeping the same link)"));
            port=new NumericUpDown{Minimum=1024,Maximum=65535,Value=settings.Port,Dock=DockStyle.Top,Margin=new Padding(0,0,0,Px(10))};Add(browser,port);
            Add(browser,FieldLabel("Target frame rate"));
            frameRate=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Top,Margin=new Padding(0,0,0,Px(10))};frameRate.Items.AddRange(new object[]{"30 fps","20 fps","15 fps"});frameRate.SelectedIndex=0;Add(browser,frameRate);
            Add(browser,FieldLabel("Video size / JPEG quality"));
            resolution=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Top,Margin=new Padding(0,0,0,Px(10))};resolution.Items.AddRange(new object[]{"1280 × 720 · balanced (55%)","960 × 540 · faster (45%)","1600 × 900 · detail (70%)"});resolution.SelectedIndex=0;Add(browser,resolution);
            var sharing=Actions(); start=Button("Start browser sharing",delegate { StartSharing(); },true); stop=Button("Stop sharing",delegate { StopSharing(); },false); stop.Enabled=false; sharing.Controls.Add(start);sharing.Controls.Add(stop); Add(browser,sharing);
            shareStatus=TextLine("OFF · Nothing is being captured.\nAfter starting, open the generated link in the TV's browser."); Add(browser,shareStatus);
            Add(browser,FieldLabel("Type this PC link into the TV browser (not the TV's IP address)"));
            url=new TextBox { ReadOnly=true,Dock=DockStyle.Top,Multiline=true,WordWrap=true,Height=Px(62),BackColor=surface,ForeColor=Color.FromArgb(145,215,255),Font=new Font("Consolas",11),Text="Start sharing to generate a session link.",Margin=new Padding(0,0,0,Px(10)),AccessibleName="TV browser link" }; Add(browser,url);
            var linkActions=Actions(); copy=Button("Copy link",delegate { if(server!=null) try { Clipboard.SetText(server.Url); } catch(Exception e) { Error(e.Message); } },false); preview=Button("Test link on this PC",delegate { if(server!=null) OpenUrl(server.Url); },false); copy.Enabled=false;preview.Enabled=false;linkActions.Controls.Add(copy);linkActions.Controls.Add(preview); Add(browser,linkActions);
            resetLink=Button("Replace saved private link",delegate {try{settings.Token=ShareSettings.NewToken();settings.Save(settingsPath);url.Text="Private link replaced. Start sharing to see the new address.";}catch(Exception e){Error(e.Message);}},false);Add(browser,resetLink);
            Add(browser,TextLine("Bookmark the link on the TV once. Keep the same PC Wi-Fi IP with a DHCP reservation in your router. The port and private path are saved locally; the app does not alter router settings. If the port is busy, it reports the problem instead of silently changing your URL."));
            Add(browser,TextLine("The link uses your PC's Wi-Fi IP (currently expected: 192.168.100.9). The TV at 192.168.100.10 opens that link to receive frames. Local preview is not proof that the TV can connect."));
            Add(browser,Heading("If it says waiting for TV"));
            Add(browser,TextLine("Check that the TV browser has the complete link, including its port and private code. If local preview works but the TV cannot load it, check the Windows firewall and Wi-Fi client isolation.\n\n30 fps is a target, not a guarantee. The TV page shows measured displayed FPS; try the faster size if it is low. No audio in browser sharing. HTTP is unencrypted: anyone with the saved link can view future sessions while you are sharing. Use Replace saved private link to revoke it. Closing this app stops browser sharing."));

            var help=Page("Diagnostics");
            Add(help,Heading("Connection details"));
            Add(help,TextLine("Checks never start a cast or capture your screen. A listening port is a connectivity signal; receiver compatibility still depends on the TV and sender."));
            diagnostics=new TextBox { Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,WordWrap=true,Dock=DockStyle.Top,Height=Px(260),BackColor=surface,ForeColor=Color.White,Text="Run Check TV connection on the Connect TV tab.",Margin=new Padding(0,0,0,Px(12)) }; Add(help,diagnostics);
            Add(help,Heading("Supported paths"));
            Add(help,TextLine("Miracast: connection through Windows Cast.\nGoogle Cast: screen sharing through Chrome.\nAirPlay: bundled AirSpan sender in a separate window.\nDLNA / DIAL: discovery only.\nBrowser sharing: persistent JPEG stream, target 30 fps, no audio.\n\nStop Windows Cast, Chrome and AirSpan sessions in their own controls. WiFi Screen's Stop button only stops its browser sharing."));
            var airplay=Page("AirPlay sender");
            Add(airplay,Heading("AirPlay screen mirroring · powered by AirSpan"));
            Add(airplay,TextLine("The package includes a corrected AirSpan sender with route-aware network selection and discovery on each adapter. It keeps the AirSpan interface and opens in its own window to handle pairing, screen capture and H.264 streaming."));
            Add(airplay,TextLine("1. Open the bundled sender below.\n2. Select TV / TCL at 192.168.100.10.\n3. Enter the TV's PIN in the sender if requested, then select Reconnect.\n4. Click Share screen in the sender.\n5. Stop sharing in that window when finished.\n\nDiscovery, pairing, media connection and visible screen playback were verified with your TCL 75C845. Audio packets were sent; audible playback has not been separately confirmed. Its capture controls are separate from Browser sharing."));
            var airActions=Actions();airActions.Controls.Add(Button("Open AirPlay sender",delegate{LaunchAirPlay();},true));airActions.Controls.Add(Button("AirSpan project",delegate{OpenUrl("https://github.com/DiznyP/AirSpan");},false));Add(airplay,airActions);
            Add(airplay,TextLine("Keep the AirPlay folder beside WifiScreen.exe. The sender is a separate process: closing WiFi Screen or pressing Stop browser sharing does not disconnect an AirPlay session. The bundled version and license/source notices are in AirPlay/."));
            timer.Interval=500; timer.Tick+=delegate { CaptureFrame(); };
            FormClosing+=delegate { if(scanCancel!=null)scanCancel.Cancel(); StopSharing();timer.Dispose(); };
            Shown+=delegate { Reflow(); };
            SizeChanged+=delegate { Reflow(); };
        }
        TableLayoutPanel Page(string title) {
            var page=new TabPage(title) { BackColor=background,ForeColor=Color.White,Padding=new Padding(Px(4)) };tabs.TabPages.Add(page);
            var scroll=new Panel { Dock=DockStyle.Fill,AutoScroll=true,BackColor=background };page.Controls.Add(scroll);
            var table=new TableLayoutPanel { AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=1,RowCount=0,Dock=DockStyle.Top,Padding=new Padding(Px(14)),Margin=Padding.Empty };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));scroll.Controls.Add(table);contentTables.Add(table);
            table.SizeChanged+=delegate { WrapLabels(table); };return table;
        }
        void Add(TableLayoutPanel table,Control control) { int row=table.RowCount++;table.RowStyles.Add(new RowStyle(SizeType.AutoSize));table.Controls.Add(control,0,row); }
        Label TextLine(string text) { return new Label { Text=text,AutoSize=true,ForeColor=muted,Margin=new Padding(0,0,0,Px(12)),UseMnemonic=false }; }
        Label Heading(string text) { var l=TextLine(text);l.Font=new Font("Segoe UI",15,FontStyle.Bold);l.ForeColor=Color.White;l.Margin=new Padding(0,Px(4),0,Px(12));return l; }
        Label FieldLabel(string text) { var l=TextLine(text);l.ForeColor=Color.White;l.Margin=new Padding(0,0,0,Px(5));return l; }
        FlowLayoutPanel Actions() { return new FlowLayoutPanel { Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=true,Margin=new Padding(0,0,0,Px(6)) }; }
        Button Button(string title,EventHandler click,bool accent) {
            var b=new Button { Text=title,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Padding=new Padding(Px(12),Px(8),Px(12),Px(8)),FlatStyle=FlatStyle.Flat,BackColor=accent?Color.FromArgb(49,113,224):surface,ForeColor=Color.White,Margin=new Padding(0,0,Px(8),Px(8)),Cursor=Cursors.Hand,UseMnemonic=false };
            b.FlatAppearance.BorderSize=0;b.Click+=click;return b;
        }
        void WrapLabels(TableLayoutPanel table) {
            int width=Math.Max(Px(180),table.ClientSize.Width-table.Padding.Horizontal);
            foreach(Control c in table.Controls) { var label=c as Label;if(label!=null && label.MaximumSize.Width!=width)label.MaximumSize=new Size(width,0); }
        }
        void Reflow() { foreach(var table in contentTables)WrapLabels(table); }
        void Error(string message) { MessageBox.Show(this,message,"WiFi Screen",MessageBoxButtons.OK,MessageBoxIcon.Information); }
        void UI(Action action) { if(IsDisposed || !IsHandleCreated)return;try {BeginInvoke(action);}catch(InvalidOperationException){} }
        void RefreshScreens() { screens.Items.Clear(); foreach(var s in Screen.AllScreens)screens.Items.Add(s.DeviceName+" · "+s.Bounds.Width+" × "+s.Bounds.Height+(s.Primary?" · primary":""));if(screens.Items.Count>0)screens.SelectedIndex=0; }
        void RefreshAdapters() { adapters.Items.Clear();foreach(var a in Adapter.All())adapters.Items.Add(a);if(adapters.Items.Count>0)adapters.SelectedIndex=0;SelectRoute(); }
        void SelectRoute() { if(server!=null)return;IPAddress tv;if(!IPAddress.TryParse(target.Text.Trim(),out tv))return;var address=NetworkCheck.Route(tv);if(address==null)return;for(int i=0;i<adapters.Items.Count;i++)if(((Adapter)adapters.Items[i]).Address.Equals(address)){adapters.SelectedIndex=i;break;} }
        async Task CheckTv() {
            IPAddress tv;if(!IPAddress.TryParse(target.Text.Trim(),out tv)||tv.AddressFamily!=AddressFamily.InterNetwork||IPAddress.IsLoopback(tv)){Error("Enter the TV's IPv4 address, such as 192.168.100.10.");return;}
            check.Enabled=false;routeStatus.Text="Checking the route and TV services...";diagnostics.Text="Checking "+tv+"...";
            try {
                if(server==null)RefreshAdapters();
                string report=await Task.Run(()=>NetworkCheck.Check(tv));if(IsDisposed)return;diagnostics.Text=report;
                var route=NetworkCheck.Route(tv);routeStatus.Text="TV: "+tv+"\nPC route: "+(route==null?"unavailable":route.ToString())+"\n"+report.Split('\n').Last(x=>x.StartsWith("Result:"));
            }catch(Exception e){if(!IsDisposed){routeStatus.Text="Check failed: "+e.Message;diagnostics.Text=e.ToString();}}
            finally{if(!IsDisposed)check.Enabled=true;}
        }
        async Task Scan() {
            scan.Enabled=false;rows.Clear();devices.Items.Clear();scanErrors.Clear();scanStatus.Text="Scanning active network adapters...";
            if(scanCancel!=null)scanCancel.Dispose();scanCancel=new CancellationTokenSource();if(server==null)RefreshAdapters();
            try {
                await Discovery.Scan(d=>UI(delegate {
                    if(rows.ContainsKey(d.Key))return;
                    string name=d.Address=="192.168.100.10"?"TCL TV · "+d.Name:d.Name;
                    var item=new ListViewItem(new[]{name,d.Protocol,d.Address});item.Tag=d;rows[d.Key]=item;devices.Items.Add(item);
                }),s=>UI(delegate{scanErrors.Add(s);}),scanCancel.Token);
                UI(delegate { scanStatus.Text=rows.Count+" receiver service(s) found"+(scanErrors.Count>0?". Errors are in Diagnostics.":". Select one for details.");if(scanErrors.Count>0)diagnostics.AppendText("\r\n\r\n"+String.Join("\r\n",scanErrors));scan.Enabled=true; });
            }catch(Exception e){if(!IsDisposed){scanStatus.Text="Scan failed: "+e.Message;scan.Enabled=true;}}
        }
        void OpenCast(){keybd_event(0x5B,0,0,UIntPtr.Zero);keybd_event(0x4B,0,0,UIntPtr.Zero);keybd_event(0x4B,0,2,UIntPtr.Zero);keybd_event(0x5B,0,2,UIntPtr.Zero);}
        void OpenChrome() {
            string[] paths={Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Google","Chrome","Application","chrome.exe"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Google","Chrome","Application","chrome.exe"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Google","Chrome","Application","chrome.exe")};
            string chrome=paths.FirstOrDefault(File.Exists);
            if(chrome==null){Error("Google Chrome was not found in the usual installation folders. Open it manually if installed, or install Chrome from google.com/chrome. Other Chromium browsers may expose Cast, depending on their settings. No software has been installed.");return;}
            try{Process.Start(chrome,"--new-window https://support.google.com/chrome/answer/3228332");}catch(Exception e){Error(e.Message);}
        }
        void OpenUrl(string link){try{Process.Start(new ProcessStartInfo(link){UseShellExecute=true});}catch(Exception e){Error(e.Message);}}
        void LaunchAirPlay(){string exe=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"AirPlay","Sender","AirSpan.exe");if(!File.Exists(exe)){Error("The corrected AirPlay sender is missing. Extract the complete WiFi Screen 1.4 package, including its AirPlay/Sender folder.");return;}try{var info=new ProcessStartInfo(exe){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(exe)};IPAddress tv;if(IPAddress.TryParse(target.Text.Trim(),out tv)&&tv.AddressFamily==AddressFamily.InterNetwork)info.EnvironmentVariables["AIRSPAN_TARGET_IP"]=tv.ToString();Process.Start(info);}catch(Exception e){Error("Unable to open the AirPlay sender: "+e.Message);}}
        void StartSharing() {
            if(server!=null)return;
            if(adapters.SelectedItem==null||screens.SelectedIndex<0){Error("Select an active network adapter and a screen first.");return;}
            try {
                var all=Screen.AllScreens;if(screens.SelectedIndex>=all.Length)throw new InvalidOperationException("Your display setup changed. Reopen the app to refresh it.");
                settings.Persistent=persistent.Checked;settings.Port=(int)port.Value;settings.Save(settingsPath);
                int width=resolution.SelectedIndex==1?960:resolution.SelectedIndex==2?1600:1280,quality=resolution.SelectedIndex==1?45:resolution.SelectedIndex==2?70:55;
                byte[] first;using(var probe=new CaptureEngine(all[screens.SelectedIndex].Bounds,width,quality))first=probe.Frame();
                server=new ScreenServer(((Adapter)adapters.SelectedItem).Address,settings.Persistent?settings.Port:0,settings.Persistent?settings.Token:ShareSettings.NewToken());server.SetFrame(first);url.Text=server.Url;started=DateTime.UtcNow;
                capture=new CapturePump(all[screens.SelectedIndex].Bounds,server,frameRate.SelectedIndex==1?20:frameRate.SelectedIndex==2?15:30,width,quality);
                persistent.Enabled=false;port.Enabled=false;resetLink.Enabled=false;frameRate.Enabled=false;resolution.Enabled=false;
                start.Enabled=false;stop.Enabled=true;copy.Enabled=true;preview.Enabled=true;adapters.Enabled=false;screens.Enabled=false;target.Enabled=false;
                UpdateShareStatus();timer.Start();
            }catch(Exception e){StopSharing();shareStatus.Text="NOT SHARING · "+e.Message+"\nRun the app from your unlocked Windows desktop. No session is active.";}
        }
        void CaptureFrame() { if(server==null)return;if(capture.Error!=null){string message=capture.Error;StopSharing();shareStatus.Text="CAPTURE STOPPED · "+message;return;}UpdateShareStatus(); }
        void UpdateShareStatus() {
            if(server==null)return;
            if(server.RecentViewer(target.Text.Trim()))shareStatus.Text="TV RECEIVING · "+target.Text.Trim()+" requested screen frames.\n"+server.FramesSent+" frames delivered · no audio. Confirm the image on your TV.";
            else if(server.FramesSent>0)shareStatus.Text="CAPTURING · Waiting for TV "+target.Text.Trim()+".\n"+server.FramesSent+" frames served (including local previews). Open the link on the TV.";
            else shareStatus.Text="CAPTURING · Waiting for a TV browser to connect.\n"+((DateTime.UtcNow-started).TotalSeconds>15?"No frame requests yet. Check the TV link, firewall and selected adapter.":"Enter the full link below into the TV's browser. No frames delivered yet.");
            shareStatus.Text+="\nCapture: "+capture.MeasuredFps.ToString("0.0")+" fps · Check displayed FPS on the TV page.";
        }
        void StopSharing() {
            timer.Stop();if(capture!=null){capture.Dispose();capture=null;}if(server!=null){server.Dispose();server=null;}
            if(start==null)return;start.Enabled=true;stop.Enabled=false;copy.Enabled=false;preview.Enabled=false;adapters.Enabled=true;screens.Enabled=true;target.Enabled=true;
            persistent.Enabled=true;port.Enabled=true;resetLink.Enabled=true;frameRate.Enabled=true;resolution.Enabled=true;
            if(!persistent.Checked)url.Text="Sharing stopped. Next start creates a new session link.";shareStatus.Text="OFF · No browser screen capture. A saved link reconnects when sharing starts again.\nStop Windows Cast, Chrome and AirPlay sessions in their own controls.";
        }
        public void LayoutTest(string path) {
            var report=new StringBuilder();
            foreach(var logical in new[]{new Size(960,740),new Size(620,520)}) {
                Size=new Size(Px(logical.Width),Px(logical.Height));
                for(int i=0;i<tabs.TabPages.Count;i++) {
                    tabs.SelectedIndex=i;Reflow();PerformLayout();Application.DoEvents();
                    foreach(Control c in contentTables[i].Controls) {
                        var l=c as Label;if(l!=null && l.Height<l.GetPreferredSize(new Size(l.Width,0)).Height)throw new Exception("Clipped label: "+l.Text);
                        if(c.Right>contentTables[i].ClientSize.Width)throw new Exception("Horizontal overflow: "+c.Text);
                    }
                    using(var image=new Bitmap(Width,Height)){DrawToBitmap(image,new Rectangle(0,0,Width,Height));image.Save(path+"-"+logical.Width+"-"+i+".png",ImageFormat.Png);}
                    report.AppendLine("PASS: "+tabs.TabPages[i].Text+" at "+logical.Width+"x"+logical.Height+" logical pixels; DPI scale "+uiScale);
                }
            }
            File.WriteAllText(path+".txt",report.ToString());
        }
        public void SmokeTest(string path) {
            persistent.Checked=false;adapters.Items.Add(new Adapter{Address=IPAddress.Loopback,Name="Loopback test"});adapters.SelectedIndex=adapters.Items.Count-1;StartSharing();
            if(server==null)throw new Exception(shareStatus.Text);
            using(var web=new WebClient()){web.Proxy=null;using(var stream=new MemoryStream(web.DownloadData(server.Url+"frame.jpg")))using(var image=Image.FromStream(stream))if(image.Width>1600||image.Height>900)throw new Exception("Invalid capture size");}
            System.Threading.Thread.Sleep(2200);double measured=capture.MeasuredFps;
            if(capture.Error!=null)throw new Exception(capture.Error);
            StopSharing();if(timer.Enabled||stop.Enabled||server!=null||capture!=null)throw new Exception("Invalid stopped state");
            var report=new StringBuilder("PASS: capture, JPEG delivery, listener stop, UI state and capture resource disposal.\r\nMeasured paced capture FPS (not TV displayed FPS): "+measured.ToString("0.0"));
            foreach(int width in new[]{1280,960})using(var bench=new CaptureEngine(Screen.AllScreens[0].Bounds,width,55)){var clock=Stopwatch.StartNew();for(int i=0;i<15;i++)bench.Frame();report.Append("\r\nUnpaced capture+JPEG "+width+" width: "+(15000/clock.Elapsed.TotalMilliseconds).ToString("0.0")+" fps");}
            File.WriteAllText(path+".txt",report.ToString());
        }
    }
    sealed class CaptureEngine : IDisposable {
        readonly Rectangle bounds;readonly Bitmap resized;readonly Graphics destination;readonly EncoderParameters parameters;readonly ImageCodecInfo codec;
        [DllImport("user32.dll")]static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")]static extern int ReleaseDC(IntPtr window,IntPtr dc);
        [DllImport("gdi32.dll")]static extern int SetStretchBltMode(IntPtr dc,int mode);
        [DllImport("gdi32.dll",SetLastError=true)]static extern bool StretchBlt(IntPtr target,int x,int y,int width,int height,IntPtr source,int sx,int sy,int sw,int sh,uint operation);
        public CaptureEngine(Rectangle bounds,int width=1280,int quality=55) {
            this.bounds=bounds;
            double scale=Math.Min(1,Math.Min((double)width/bounds.Width,(width*9.0/16)/bounds.Height));resized=new Bitmap(Math.Max(1,(int)(bounds.Width*scale)),Math.Max(1,(int)(bounds.Height*scale)));destination=Graphics.FromImage(resized);
            codec=ImageCodecInfo.GetImageEncoders().First(x=>x.MimeType=="image/jpeg");parameters=new EncoderParameters(1);parameters.Param[0]=new EncoderParameter(System.Drawing.Imaging.Encoder.Quality,(long)quality);
        }
        public byte[] Frame(){IntPtr source=GetDC(IntPtr.Zero);if(source==IntPtr.Zero)throw new InvalidOperationException("Windows desktop capture is unavailable.");try{IntPtr target=destination.GetHdc();try{SetStretchBltMode(target,3);if(!StretchBlt(target,0,0,resized.Width,resized.Height,source,bounds.X,bounds.Y,bounds.Width,bounds.Height,0x40CC0020))throw new System.ComponentModel.Win32Exception();}finally{destination.ReleaseHdc(target);}}finally{ReleaseDC(IntPtr.Zero,source);}using(var stream=new MemoryStream()){resized.Save(stream,codec,parameters);return stream.ToArray();}}
        public void Dispose(){destination.Dispose();resized.Dispose();parameters.Dispose();}
    }
    static class NetworkCheck {
        public static IPAddress Route(IPAddress target) {
            try{using(var socket=new Socket(AddressFamily.InterNetwork,SocketType.Dgram,ProtocolType.Udp)){socket.Connect(new IPEndPoint(target,8009));return ((IPEndPoint)socket.LocalEndPoint).Address;}}catch(SocketException){return null;}
        }
        public static string Check(IPAddress target) {
            var report=new StringBuilder();report.AppendLine("TV: "+target);report.AppendLine("PC route address: "+Route(target));report.AppendLine();
            bool reachable=false;
            foreach(int port in new[]{8009,8008,7000}) {
                string name=port==8009?"Google Cast":port==8008?"DIAL / device information":"AirPlay";
                using(var client=new TcpClient())try{var result=client.BeginConnect(target,port,null,null);using(result.AsyncWaitHandle){if(!result.AsyncWaitHandle.WaitOne(1500)){report.AppendLine(name+" ("+port+"): timed out");continue;}client.EndConnect(result);report.AppendLine(name+" ("+port+"): TCP reachable");reachable=true;}}catch(SocketException e){report.AppendLine(name+" ("+port+"): "+e.SocketErrorCode);}
            }
            report.AppendLine();report.AppendLine("Browser sharing works in the reverse direction: the TV must open the PC's session link.");report.AppendLine("This test cannot prove the TV can reach the PC through Windows Firewall.");
            report.AppendLine(reachable?"Result: TV services reachable. Use Google Cast setup or open the browser session link on the TV.":"Result: No checked TV ports responded. Check the IP, power, network and casting settings.");return report.ToString();
        }
    }
}
