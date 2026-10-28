using System.Runtime.InteropServices;
using System.Text;
namespace DesktopBoxes;
internal static class Native {
 [DllImport("user32.dll")] internal static extern bool ReleaseCapture();
 [DllImport("user32.dll")] internal static extern nint SendMessage(nint h,uint m,nint w,nint l);
 [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback,nint data);
 delegate bool EnumProc(nint h,nint data);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern nint FindWindowEx(nint parent,nint after,string cls,string? text);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(nint h,StringBuilder name,int size);
 [DllImport("user32.dll")] static extern nint GetForegroundWindow();
 [DllImport("user32.dll")] static extern bool SetWindowPos(nint h,nint after,int x,int y,int cx,int cy,uint flags);
 [DllImport("user32.dll")] static extern int GetWindowLong(nint h,int index);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(nint h,out uint pid);
 [DllImport("user32.dll")] static extern bool ClientToScreen(nint h,ref Point p);
 [DllImport("kernel32.dll")] static extern nint OpenProcess(uint access,bool inherit,uint pid);
 [DllImport("kernel32.dll")] static extern nint VirtualAllocEx(nint process,nint address,nuint size,uint type,uint protect);
 [DllImport("kernel32.dll")] static extern bool VirtualFreeEx(nint process,nint address,nuint size,uint type);
 [DllImport("kernel32.dll")] static extern bool ReadProcessMemory(nint process,nint address,byte[] data,nuint size,out nuint read);
 [DllImport("kernel32.dll")] static extern bool CloseHandle(nint h);
 internal static void KeepOnDesktop(IEnumerable<Box> boxes){
 var fg=GetForegroundWindow();var cls=new StringBuilder(256);GetClassName(fg,cls,256);
 bool desktop=cls.ToString() is "Progman" or "WorkerW";
 foreach(var box in boxes.Where(b=>b.Visible))if(fg!=box.Handle)SetWindowPos(box.Handle,desktop?0:1,0,0,0,0,0x13);
 }
 internal static void ClearCoveredIcons(Rectangle[] boxes){
 if(boxes.Length==0)return;
 nint list=0;
 EnumWindows((h,_)=>{var view=FindWindowEx(h,0,"SHELLDLL_DefView",null);if(view!=0)list=FindWindowEx(view,0,"SysListView32",null);return list==0;},0);
 if(list==0 || (GetWindowLong(list,-16)&0x100)!=0)return; // Explorer auto-arrange owns placement.
 GetWindowThreadProcessId(list,out var pid);var process=OpenProcess(0x38,false,pid);if(process==0)return;
 var remote=VirtualAllocEx(process,0,8,0x3000,4);
 try{
 if(remote==0)return;
 int count=(int)SendMessage(list,0x1004,0,0);if(count>10000)return;
 var origin=Point.Empty;ClientToScreen(list,ref origin);
 var spacing=(long)SendMessage(list,0x1033,0,0);int sx=Math.Max(72,(int)(spacing&65535)),sy=Math.Max(80,(int)((spacing>>16)&65535));
 var positions=new List<Point>();var data=new byte[8];
 for(int i=0;i<count;i++){if(SendMessage(list,0x1010,i,remote)==0 || !ReadProcessMemory(process,remote,data,8,out var read) || read!=8)return;positions.Add(new Point(BitConverter.ToInt32(data,0)+origin.X,BitConverter.ToInt32(data,4)+origin.Y));}
 for(int i=0;i<count;i++){
 var rect=new Rectangle(positions[i],new Size(sx,sy));if(!boxes.Any(b=>b.IntersectsWith(rect)))continue;
 Point? free=null;
 foreach(var screen in Screen.AllScreens){var area=screen.WorkingArea;for(int y=area.Top;y+sy<=area.Bottom && free==null;y+=sy)for(int x=area.Left;x+sx<=area.Right;x+=sx){var candidate=new Rectangle(x,y,sx,sy);if(boxes.Any(b=>b.IntersectsWith(candidate)) || positions.Where((_,j)=>j!=i).Any(p=>new Rectangle(p,new Size(sx,sy)).IntersectsWith(candidate)))continue;free=new Point(x,y);break;}if(free!=null)break;}
 if(free is Point p){int x=p.X-origin.X,y=p.Y-origin.Y;if(x<0||y<0||x>32767||y>32767)continue;if(SendMessage(list,0x100F,i,(nint)((y<<16)|(x&65535)))!=0)positions[i]=p;}
 }
 }finally{if(remote!=0)VirtualFreeEx(process,remote,0,0x8000);CloseHandle(process);}
 }
}
