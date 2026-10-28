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
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern nint FindFirstFileExW(string name,int level,out FindData data,int search,nint filter,int flags);
 [DllImport("kernel32.dll")] static extern bool FindClose(nint h);
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode,Pack=4)] struct FindData{public uint Attributes;public long Created,Accessed,Written;public uint SizeHigh,SizeLow,Reserved0,Reserved1;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]public string Name;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=14)]public string ShortName;}
 /// <summary>The reparse tag of a reparse-point file or folder (FindExInfoBasic puts it in dwReserved0).</summary>
 internal static uint ReparseTag(string path){
 var h=FindFirstFileExW(path.Length>=260&&!path.StartsWith(@"\\")?@"\\?\"+path:path,1,out var data,0,0,0);
 if(h==-1)throw new IOException("Couldn't read "+path,Marshal.GetHRForLastWin32Error());
 FindClose(h);return data.Reserved0;
 }
 /// <summary>Shows Explorer's Properties dialog; type 2 = SHOP_FILEPATH.</summary>
 [DllImport("shell32.dll",CharSet=CharSet.Unicode)] internal static extern bool SHObjectProperties(nint hwnd,uint type,string name,string? page);
 internal static void KeepOnDesktop(IEnumerable<Box> boxes){
 var fg=GetForegroundWindow();var cls=new StringBuilder(256);GetClassName(fg,cls,256);
 bool desktop=cls.ToString() is "Progman" or "WorkerW";
 foreach(var box in boxes.Where(b=>b.Visible))if(fg!=box.Handle)SetWindowPos(box.Handle,desktop?0:1,0,0,0,0,0x13);
 }
}


