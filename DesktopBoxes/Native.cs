using System.Runtime.InteropServices;
using System.Text;
namespace DesktopBoxes;
internal static class Native {
 [DllImport("user32.dll")] internal static extern bool ReleaseCapture();
 /// <summary>33 = corner preference (2 rounds), 34 = border colour (COLORREF), 20 = immersive dark title bar.</summary>
 [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(nint hwnd,int attr,ref int value,int size);
 [DllImport("user32.dll")] internal static extern nint SendMessage(nint h,uint m,nint w,nint l);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(nint h,StringBuilder name,int size);
 [DllImport("user32.dll")] static extern nint GetForegroundWindow();
 [DllImport("user32.dll")] static extern bool SetWindowPos(nint h,nint after,int x,int y,int cx,int cy,uint flags);
 internal static void KeepOnDesktop(IEnumerable<Box> boxes){
 var fg=GetForegroundWindow();var cls=new StringBuilder(256);GetClassName(fg,cls,256);
 bool desktop=cls.ToString() is "Progman" or "WorkerW";
 foreach(var box in boxes.Where(b=>b.Visible))if(fg!=box.Handle)SetWindowPos(box.Handle,desktop?0:1,0,0,0,0,0x13);
 }
}


