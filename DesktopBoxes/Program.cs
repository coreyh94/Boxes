using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;
namespace DesktopBoxes;
static class Program {
 [STAThread] static void Main(string[] args) { using var mutex = new Mutex(true, "Local\\DesktopBoxes", out var first); if(!first)return; ApplicationConfiguration.Initialize(); Application.Run(new Boxes(args.Contains("--smoke-test"))); }
}
public class State {
 public string Name {get;set;} = "My box";
 public int X {get;set;} = 48; public int Y {get;set;} = 48;
 public int Width {get;set;} = 384; public int Height {get;set;} = 288;
 public int Colour {get;set;} = Color.SteelBlue.ToArgb();
 public List<string> Items {get;set;} = [];
}
public sealed class Boxes : ApplicationContext {
 internal readonly List<Box> Windows = [];
 readonly NotifyIcon tray;
 readonly System.Windows.Forms.Timer timer = new() { Interval = 2000 };
 static readonly string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopBoxes");
 readonly string file = Path.Combine(folder,"layout.json");
 public Boxes(bool smoke = false) {
 if(smoke) { var test = new Box(this, new State()); Windows.Add(test); test.Shown += (_,_) => test.BeginInvoke(() => { if(test.Controls.Count < 3) throw new InvalidOperationException("Layout missing"); Windows.Clear(); test.Dispose(); ExitThread(); }); test.Show(); tray = new NotifyIcon(); return; }
 Directory.CreateDirectory(folder);
 var menu = new ContextMenuStrip();
 menu.Items.Add("New box",null,(_,_)=>Add());
 menu.Items.Add("Show boxes",null,(_,_)=> {foreach(var b in Windows)b.Show();});
 menu.Items.Add("Hide boxes",null,(_,_)=> {foreach(var b in Windows)b.Hide();});
 var startup = new ToolStripMenuItem("Start with Windows") {CheckOnClick=true};
 using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) startup.Checked=key?.GetValue("DesktopBoxes")!=null;
 startup.Click+=(_,_)=>{using var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); if(startup.Checked)key.SetValue("DesktopBoxes","\""+Application.ExecutablePath+"\"");else key.DeleteValue("DesktopBoxes",false);};
 menu.Items.Add(startup);
 menu.Items.Add("Exit",null,(_,_)=>ExitThread());
 tray=new NotifyIcon{Icon=SystemIcons.Application,Text="Desktop Boxes",Visible=true,ContextMenuStrip=menu};
 if(File.Exists(file))try{foreach(var s in JsonSerializer.Deserialize<List<State>>(File.ReadAllText(file))??[])Add(s);}catch(JsonException){File.Copy(file,file+".invalid-"+DateTime.Now.Ticks);}
 if(Windows.Count==0)Add();
 timer.Tick+=(_,_)=>{Native.KeepOnDesktop(Windows); Native.ClearCoveredIcons(Windows.Where(b=>b.Visible).Select(b=>b.Bounds).ToArray());};timer.Start();
 }
 internal void Add(State? state=null){var b=new Box(this,state??new State{X=48+Windows.Count*48,Y=48+Windows.Count*48});Windows.Add(b);b.Show();}
 internal void Save(){File.WriteAllText(file+".tmp",JsonSerializer.Serialize(Windows.Select(b=>b.State),new JsonSerializerOptions{WriteIndented=true}));File.Move(file+".tmp",file,true);}
 protected override void ExitThreadCore(){if(Windows.Count>0)Save();timer.Dispose();tray.Dispose();foreach(var b in Windows.ToArray())b.Dispose();base.ExitThreadCore();}
}
public sealed class Box:Form {
 internal readonly State State; readonly Boxes owner;
 readonly FlowLayoutPanel items=new(){Dock=DockStyle.Fill,AutoScroll=true,Padding=new Padding(8)};
 readonly TextBox title=new(){Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,ForeColor=Color.White};
 readonly Panel header=new(){Dock=DockStyle.Top,Height=36,Padding=new Padding(24,10,8,4),Cursor=Cursors.SizeAll};
 Point drag; Grid? grid;
 protected override bool ShowWithoutActivation=>true;
 protected override CreateParams CreateParams{get{var cp=base.CreateParams;cp.ExStyle|=0x80;return cp;}}
 public Box(Boxes owner,State state){
 this.owner=owner;State=state;FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;MinimumSize=new Size(192,144);
 Bounds=new Rectangle(state.X,state.Y,state.Width,state.Height);
 if(!Screen.AllScreens.Any(s=>s.WorkingArea.IntersectsWith(Bounds)))Location=Screen.PrimaryScreen!.WorkingArea.Location+new Size(48,48);
 Controls.Add(items);Controls.Add(header);header.Controls.Add(title);title.Text=state.Name;
 title.TextChanged+=(_,_)=>{State.Name=title.Text;owner.Save();};
 header.MouseDown+=(_,e)=>{if(e.Button!=MouseButtons.Left)return;drag=e.Location;header.Capture=true;grid=new Grid(Screen.FromControl(this).WorkingArea);grid.Show();};
 header.MouseMove+=(_,e)=>{if(header.Capture)Location=new Point(Left+e.X-drag.X,Top+e.Y-drag.Y);};
 header.MouseUp+=(_,_)=>{header.Capture=false;grid?.Close();grid=null;Snap();};
 var grip=new Panel{Dock=DockStyle.Bottom,Height=10,Cursor=Cursors.SizeNWSE};Controls.Add(grip);
 grip.MouseDown+=(_,_)=>{Native.ReleaseCapture();Native.SendMessage(Handle,0xA1,17,0);Snap();};
 var menu=new ContextMenuStrip();menu.Items.Add("New box",null,(_,_)=>owner.Add());
 foreach(var c in new[]{Color.SteelBlue,Color.SeaGreen,Color.SlateBlue,Color.IndianRed,Color.DarkGoldenrod,Color.DarkSlateGray}){var entry=menu.Items.Add(c.Name);entry.ForeColor=c;entry.Click+=(_,_)=>{State.Colour=c.ToArgb();Recolour();owner.Save();};}
 menu.Items.Add("Delete box (keep files)",null,(_,_)=>{owner.Windows.Remove(this);owner.Save();Close();});
 ContextMenuStrip=menu;header.ContextMenuStrip=menu;items.ContextMenuStrip=menu;
 AllowDrop=true;items.AllowDrop=true;
 DragEventHandler enter=(_,e)=>e.Effect=e.Data?.GetDataPresent(DataFormats.FileDrop)==true?DragDropEffects.Link:DragDropEffects.None;
 DragEnter+=enter;items.DragEnter+=enter;
 DragEventHandler drop=(_,e)=>{if(e.Data?.GetData(DataFormats.FileDrop)is string[] paths){foreach(var p in paths)if(!State.Items.Contains(p,StringComparer.OrdinalIgnoreCase))State.Items.Add(p);RenderItems();owner.Save();}};
 DragDrop+=drop;items.DragDrop+=drop;Recolour();RenderItems();
 }
 void Recolour(){var c=Color.FromArgb(State.Colour);BackColor=c;header.BackColor=c;title.BackColor=c;items.BackColor=Color.FromArgb(c.R/3,c.G/3,c.B/3);}
 void Snap(){var area=Screen.FromControl(this).WorkingArea;int Round(int v)=>(int)Math.Round(v/48d)*48;Size=new Size(Math.Max(192,Round(Width)),Math.Max(144,Round(Height)));Location=new Point(area.Left+Round(Left-area.Left),area.Top+Round(Top-area.Top));State.X=Left;State.Y=Top;State.Width=Width;State.Height=Height;owner.Save();}
 void RenderItems(){
 foreach(Control c in items.Controls.Cast<Control>().ToArray())c.Dispose();
 foreach(var path in State.Items.ToArray()){
 var button=new Button{Width=80,Height=80,Text=Path.GetFileNameWithoutExtension(path),ForeColor=Color.White,FlatStyle=FlatStyle.Flat,TextImageRelation=TextImageRelation.ImageAboveText,Margin=new Padding(3),AutoEllipsis=true};button.FlatAppearance.BorderSize=0;
 try{using var icon=Icon.ExtractAssociatedIcon(path);button.Image=icon?.ToBitmap();}catch{}
 button.Disposed+=(_,_)=>button.Image?.Dispose();
 button.Click+=(_,_)=>{try{Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}catch{button.Text="Missing: "+Path.GetFileName(path);}};
 var menu=new ContextMenuStrip();menu.Items.Add("Remove from box",null,(_,_)=>{State.Items.Remove(path);RenderItems();owner.Save();});button.ContextMenuStrip=menu;button.Disposed+=(_,_)=>menu.Dispose();items.Controls.Add(button);
 }
 }
}
sealed class Grid:Form {
 public Grid(Rectangle area){StartPosition=FormStartPosition.Manual;Bounds=area;FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;BackColor=Color.Black;Opacity=.18;Enabled=false;}
 protected override bool ShowWithoutActivation=>true;
 protected override CreateParams CreateParams{get{var cp=base.CreateParams;cp.ExStyle|=0x080000A0;return cp;}}
 protected override void OnPaint(PaintEventArgs e){for(int x=0;x<Width;x+=48)e.Graphics.DrawLine(Pens.White,x,0,x,Height);for(int y=0;y<Height;y+=48)e.Graphics.DrawLine(Pens.White,0,y,Width,y);}
}

