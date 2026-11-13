using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Diagnostics;
using System.IO;

namespace VisionaryRouter.Desktop;
public static class Program {
 [STAThread] public static void Main() { using var mutex=new Mutex(true,@"LocalVisionaryRouterDesktop",out var first); if(!first){MessageBox.Show("Visionary Router Desktop is already open.");return;}var app=new Application();app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/VisionaryRouter.Desktop;component/Theme.xaml",UriKind.Relative)});app.Run(new RouterWindow()); }
}
public sealed class Device {
 public Guid Id {get;set;} public string Name {get;set;}=""; public string Type {get;set;}=""; public string IpAddress {get;set;}="";
 public int ControlPort {get;set;}=8000; public string? CurrentEncoderIp {get;set;} public string? Model {get;set;} public string? Firmware {get;set;} public string? LastError {get;set;} public int SortOrder {get;set;} public string OnlineState {get;set;}=""; public string? VideoInput {get;set;} public string? CurrentSourceName {get;set;} public bool Enabled {get;set;}
}
public sealed partial class RouterWindow : Window {
 HttpClient http = new(){Timeout=TimeSpan.FromMinutes(10)};
 readonly StackPanel sources=new(), destinations=new(); readonly TextBlock summary=new(), status=new(); readonly Button route=new(){Content="Route"};
 readonly HashSet<Guid> selected=new(); List<Device> devices=[]; Guid? source; bool busy, refreshing; Process? owned;
 readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(3)};
 static Brush Brush(string c)=>new SolidColorBrush((Color)ColorConverter.ConvertFromString(c));
 static TextBlock Label(string t,double size=14)=>new(){Text=t,FontSize=size,Foreground=Brush("#E5EAF3"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,3,0,5)};
 static Button Button(string title,Action action){var b=new Button{Content=title,Padding=new Thickness(14,7,14,7),Margin=new Thickness(0,5,8,5)};b.Click+=(_,_)=>action();return b;}
 public RouterWindow(){
 Title="Visionary Router • Desktop";Width=1180;Height=780;MinWidth=720;MinHeight=560;Background=Brush("#10151E");FontFamily=new FontFamily("Segoe UI");
 var root=new DockPanel{Margin=new Thickness(20)};Content=root;
 var header=new StackPanel();DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);header.Children.Add(Label("Visionary Router",26));header.Children.Add(Label("Select a source, choose destinations, then route.",14));
 var actions=new WrapPanel();header.Children.Add(actions);actions.Children.Add(Button("Deck mode",OpenDeck));actions.Children.Add(Button("Add device",AddDevice));actions.Children.Add(Button("Devices",ManageDevices));actions.Children.Add(Button("Groups & presets",SavedRoutes));actions.Children.Add(Button("Settings & diagnostics",Settings));actions.Children.Add(Button("Clear selection",()=>{source=null;selected.Clear();Render();}));
 AddSearch(header);
 var footer=new StackPanel{Margin=new Thickness(0,14,0,0)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);summary.Foreground=Brush("#E5EAF3");summary.FontSize=16;summary.TextWrapping=TextWrapping.Wrap;footer.Children.Add(summary);route.Background=Brush("#245FA3");route.BorderBrush=Brush("#75B3FF");route.FontSize=16;route.Padding=new Thickness(24,10,24,10);route.Margin=new Thickness(0,12,0,8);route.HorizontalAlignment=HorizontalAlignment.Right;route.Click+=async(_,_)=>await Route();footer.Children.Add(route);status.Foreground=Brush("#9CAEC7");status.TextWrapping=TextWrapping.Wrap;footer.Children.Add(new ScrollViewer{Content=status,MaxHeight=80,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
 var grid=new Grid();grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new());root.Children.Add(grid);
 void Pane(string title,StackPanel list,int column){var p=new DockPanel{Margin=new Thickness(column==0?0:12,10,column==0?12:0,0)};Grid.SetColumn(p,column);grid.Children.Add(p);var h=Label(title,20);DockPanel.SetDock(h,Dock.Top);p.Children.Add(h);p.Children.Add(new ScrollViewer{Content=list,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});}
 Pane("1   Sources",sources,0);Pane("2   Destinations",destinations,1);
 SizeChanged+=(_,_)=>{grid.ColumnDefinitions[0].Width=new GridLength(ActualWidth<950?1:0.9,GridUnitType.Star);};
 Loaded+=async(_,_)=>{try{await Connect();await Refresh();timer.Start();}catch(Exception e){status.Text="Cannot connect: "+e.Message;}};
 timer.Tick+=async(_,_)=>await Refresh();Closed+=(_,_)=>{timer.Stop();http.Dispose();if(owned is {HasExited:false})owned.Kill(true);};
 }
 async Task Connect(){
 try {SetPort();using var r=await http.GetAsync("/api/system/status");if(r.StatusCode==System.Net.HttpStatusCode.Unauthorized){Login();return;}r.EnsureSuccessStatusCode();using var identity=JsonDocument.Parse(await r.Content.ReadAsStringAsync());if(!identity.RootElement.TryGetProperty("deviceCount",out _))throw new IOException("The configured port is used by another application.");return;}catch(HttpRequestException){}
 var path=Path.Combine(AppContext.BaseDirectory,"server","VisionaryRouter.exe");
 if(!File.Exists(path))throw new IOException("Router server is missing from the server folder.");
 owned=Process.Start(new ProcessStartInfo(path,"--Router:OpenBrowserOnStart=false --Router:DesktopOnly=true"){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(path)!});
 for(var i=0;i<40;i++){await Task.Delay(250);try{SetPort();using var r=await http.GetAsync("/api/system/status");if(r.StatusCode==System.Net.HttpStatusCode.Unauthorized){Login();return;}if(r.IsSuccessStatusCode)return;}catch(HttpRequestException){}}
 throw new IOException("Server did not start on localhost:8088. Check the port in Settings and reconnect.");
 }
 async Task Refresh(){if(refreshing||busy)return;refreshing=true;try{var next=await http.GetFromJsonAsync<List<Device>>("/api/devices")??[];var signature=JsonSerializer.Serialize(next);if(signature==lastSnapshot&&connected)return;lastSnapshot=signature;connected=true;devices=next;selected.RemoveWhere(id=>!devices.Any(d=>d.Id==id&&d.Enabled));if(!devices.Any(d=>d.Id==source&&d.Enabled))source=null;Render();}catch(Exception e){connected=false;status.Text="Connection lost: "+e.Message;route.IsEnabled=false;deck?.UpdateState();}finally{refreshing=false;}}
 void Render(){UpdatePages();sources.Children.Clear();destinations.Children.Clear();foreach(var d in VisibleDevices()){
 var panel=new StackPanel{Margin=new Thickness(10)};var card=new Border{Background=Brush("#1C2533"),BorderBrush=Brush(source==d.Id||selected.Contains(d.Id)?"#69AEFF":"#344257"),BorderThickness=new Thickness(2),CornerRadius=new CornerRadius(10),Margin=new Thickness(0,0,0,12),Child=panel};
 panel.Children.Add(Label(d.Name,17));var health=Label($"● {d.OnlineState}   ·   {d.IpAddress}",12);health.Foreground=Brush(d.OnlineState=="Online"?"#8DD8B6":"#EDAB9F");panel.Children.Add(health);
 if(d.Type=="Encoder"){
 panel.Children.Add(Button(source==d.Id?"✓ Selected source":"Select source",()=>{if(busy)return;source=d.Id;Render();}));
 panel.Children.Add(Label("Live input · switches immediately",11));var inputs=new WrapPanel();panel.Children.Add(inputs);
 foreach(var input in new[]{"HDMI1","HDMI2","USB-C"}){var value=input;var b=Button((d.VideoInput==input?"✓ ":"")+input.Replace("HDMI","HDMI "),async()=>await Switch(d,value));b.IsEnabled=connected&&!busy&&d.OnlineState!="Offline";if(d.VideoInput==input){b.Background=Brush("#254E48");b.BorderBrush=Brush("#6FAF9E");}inputs.Children.Add(b);}sources.Children.Add(card);
 }else if(d.Type=="Decoder") {panel.Children.Add(Label("Showing: "+(d.CurrentSourceName??"No source reported")));panel.Children.Add(Button(selected.Contains(d.Id)?"✓ Destination selected":"Select destination",()=>{if(busy)return;if(!selected.Add(d.Id))selected.Remove(d.Id);Render();}));destinations.Children.Add(card);}}
 if(sources.Children.Count==0)sources.Children.Add(Label("No sources yet. Use Add device to register an encoder."));if(destinations.Children.Count==0)destinations.Children.Add(Label("No destinations yet. Use Add device to register a decoder."));
 var name=devices.Find(d=>d.Id==source)?.Name;summary.Text=name==null?"Choose a source with Select source.":$"{name} → {selected.Count} destination(s)";route.Content=$"Route to {selected.Count} destination(s)";route.IsEnabled=connected&&!busy&&source!=null&&selected.Count>0;deck?.UpdateState();
 }
 async Task<JsonElement> Post(string path,object value){using var r=await http.PostAsJsonAsync(path,value);var text=await r.Content.ReadAsStringAsync();if(!r.IsSuccessStatusCode)throw new Exception(text);return JsonSerializer.Deserialize<JsonElement>(text);}
 async Task Switch(Device d,string input){if(busy||!connected)return;busy=true;Render();try{var r=await Post($"/api/devices/{d.Id}/video-input",new{input});status.Text=r.GetProperty("verified").GetBoolean()?$"{d.Name}: {input} verified. Routing selection unchanged.":r.GetProperty("error").GetString();}catch(Exception e){status.Text=e.Message;}finally{busy=false;lastSnapshot="";await Refresh();}}
 async Task Route(){if(source==null||busy||!connected)return;busy=true;Render();try{var r=await Post("/api/routes",new{encoderId=source,decoderIds=selected.ToArray()});status.Text=$"Verified: {r.GetProperty("successCount")}; failed: {r.GetProperty("failureCount")}. "+string.Join("; ",r.GetProperty("results").EnumerateArray().Where(x=>!x.GetProperty("verified").GetBoolean()).Select(x=>$"{x.GetProperty("decoder")}: {x.GetProperty("error")}"));if(r.TryGetProperty("warning",out var warning)&&warning.ValueKind==JsonValueKind.String)status.Text+=" "+warning.GetString();selected.Clear();}catch(Exception e){status.Text=e.Message;}finally{busy=false;lastSnapshot="";await Refresh();}}
 void AddDevice(){var dialog=new Window{Title="Add device",Owner=this,Width=420,Height=440,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=Background};var p=new StackPanel{Margin=new Thickness(24)};dialog.Content=new ScrollViewer{Content=p,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};var name=new TextBox();var ip=new TextBox();var type=new ComboBox{ItemsSource=new[]{"Encoder","Decoder"},SelectedIndex=0};p.Children.Add(Label("Name"));p.Children.Add(name);p.Children.Add(Label("IP address"));p.Children.Add(ip);p.Children.Add(Label("Device type"));p.Children.Add(type);var controlPort=new TextBox{Text="8000"};p.Children.Add(Label("Control port"));p.Children.Add(controlPort);var error=Label("");p.Children.Add(error);var add=Button("Add device",()=>{});add.Click+=async(_,_)=>{add.IsEnabled=false;try{if(!int.TryParse(controlPort.Text,out var port)||port<1||port>65535)throw new Exception("Control port must be 1–65535.");await Post("/api/devices",new{name=name.Text,ipAddress=ip.Text,type=type.SelectedItem.ToString(),controlPort=port});dialog.Close();await Refresh();}catch(Exception e){error.Text=e.Message;add.IsEnabled=true;}};p.Children.Add(add);dialog.ShowDialog();}
}
