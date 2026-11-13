using System.Reflection;
using System.Diagnostics;
using System.Windows.Controls;
using VisionaryRouter.Desktop;
class Check {
 [STAThread] static void Main(){var w=new RouterWindow();var t=w.GetType();const BindingFlags f=BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly;void Set(string n,object v)=>t.GetField(n,f)!.SetValue(w,v);T Get<T>(string n)=>(T)t.GetField(n,f)!.GetValue(w)!;void Render()=>t.GetMethod("Render",f)!.Invoke(w,null);void Assert(bool ok,string msg){if(!ok)throw new Exception(msg);Console.WriteLine("PASS "+msg);}
 var records=Enumerable.Range(0,10000).Select(i=>new Device{Id=Guid.NewGuid(),Name="Device "+i,IpAddress="10.0."+(i/255)+"."+(i%255),Type=i%2==0?"Encoder":"Decoder",Enabled=true,SortOrder=i}).ToList();Set("devices",records);Set("connected",true);var sw=Stopwatch.StartNew();Render();sw.Stop();Assert(Get<StackPanel>("sources").Children.Count==12&&Get<StackPanel>("destinations").Children.Count==12,"10,000 devices produce only 24 routing cards");Console.WriteLine("Render milliseconds: "+sw.ElapsedMilliseconds);Set("page",400);Render();Assert(Get<StackPanel>("sources").Children.Count<=12,"bounded cards on later pages");Set("source",records[0].Id);Get<HashSet<Guid>>("selected").Add(records[1].Id);Set("query","Device 9999");Set("page",0);Render();Assert(Get<StackPanel>("destinations").Children.Count==1,"search finds a device beyond the first page");Assert(Get<Guid?>("source")==records[0].Id&&Get<HashSet<Guid>>("selected").Contains(records[1].Id),"search preserves routing selections");Set("query","");Set("connected",false);Render();Assert(!Get<Button>("route").IsEnabled,"routing disabled when disconnected");w.Close();
 var deckDevices=records.Take(6).ToList();Guid? deckSource=null;var deckTargets=new HashSet<Guid>();int routes=0;bool ready=true;bool running=false;
 var deck=new DeckWindow(()=>new DeckState(deckDevices,deckSource,deckTargets,ready,running,""), id=>deckSource=id,id=>{if(!deckTargets.Add(id))deckTargets.Remove(id);},all=>{deckTargets.Clear();if(all)foreach(var d in deckDevices.Where(d=>d.Type=="Decoder"))deckTargets.Add(d.Id);else deckSource=null;},()=>{routes++;return Task.CompletedTask;});
 T DeckField<T>(string n)=>(T)typeof(DeckWindow).GetField(n,f)!.GetValue(deck)!;
 void Click(System.Windows.Controls.Button b)=>b.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
 var encoderTiles=DeckField<WrapPanel>("sources");Click((Button)encoderTiles.Children[0]);
 var decoderTiles=DeckField<WrapPanel>("destinations");Click((Button)decoderTiles.Children[0]);Click((Button)decoderTiles.Children[1]);
 Assert(deckSource==deckDevices[0].Id&&deckTargets.Count==2,"deck selects one encoder and multiple decoders");
 var send=DeckField<Button>("send");Assert(send.IsEnabled,"deck enables route with encoder and destinations");Click(send);Assert(routes==1,"deck invokes shared route action once");
 Click((Button)decoderTiles.Children[0]);Assert(deckTargets.Count==1,"deck decoder selection toggles independently");
 running=true;deck.UpdateState();Assert(!send.IsEnabled&&DeckField<WrapPanel>("sources").Children.OfType<Button>().All(b=>!b.IsEnabled),"deck locks controls during operation");
 running=false;ready=false;deck.UpdateState();Assert(!send.IsEnabled,"deck disables route on disconnect");
 bool HasInput(System.Windows.DependencyObject node){for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(node);i++){var child=System.Windows.Media.VisualTreeHelper.GetChild(node,i);if(child is TextBlock text&&(text.Text.Contains("HDMI")||text.Text.Contains("USB-C")))return true;if(HasInput(child))return true;}return false;}
 Assert(!HasInput((System.Windows.DependencyObject)deck.Content),"deck has no HDMI or USB-C controls");
 Assert(deck.MinWidth==420&&deck.ResizeMode==System.Windows.ResizeMode.CanResize,"deck is resizable down to compact size");deck.Close();}
}
