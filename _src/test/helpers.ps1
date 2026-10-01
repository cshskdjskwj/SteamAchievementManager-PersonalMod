# 窗口操作辅助：枚举控件、读文本、模拟点击，供自动化测试使用
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public class T {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EnumProc cb, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder b, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, string l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr SendMessageW(IntPtr h, uint m, IntPtr w, StringBuilder l);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr SetFocus(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out R r);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
  [StructLayout(LayoutKind.Sequential)] public struct R { public int L,T,Rt,B; }
  public static List<IntPtr> Kids(IntPtr p){ var l=new List<IntPtr>(); EnumChildWindows(p,(h,x)=>{l.Add(h);return true;},IntPtr.Zero); return l; }
  public static string Cls(IntPtr h){ var b=new StringBuilder(256); GetClassName(h,b,256); return b.ToString().Replace("WindowsForms10.",""); }
  public static string Txt(IntPtr h){ var b=new StringBuilder(4000); SendMessageW(h,0x000D,new IntPtr(b.Capacity),b); return b.ToString(); }
  public static void SetText(IntPtr h, string v){ SendMessageW(h,0x000C,IntPtr.Zero,v); }
  public static void Click(IntPtr h){
    var r=new R(); GetWindowRect(h,out r);
    SetCursorPos((r.L+r.Rt)/2,(r.T+r.B)/2); System.Threading.Thread.Sleep(150);
    mouse_event(0x0002,0,0,0,IntPtr.Zero); System.Threading.Thread.Sleep(80);
    mouse_event(0x0004,0,0,0,IntPtr.Zero);
  }
  public static void SaveWin(IntPtr h,string path){
    var r=new R(); GetWindowRect(h,out r);
    int w=r.Rt-r.L, ht=r.B-r.T;
    using(var bmp=new System.Drawing.Bitmap(w,ht)) using(var g=System.Drawing.Graphics.FromImage(bmp)){
      g.CopyFromScreen(r.L,r.T,0,0,new System.Drawing.Size(w,ht)); bmp.Save(path,System.Drawing.Imaging.ImageFormat.Png); }
  }
  public static void SaveScreen(string path,int x,int y,int w,int ht){
    using(var bmp=new System.Drawing.Bitmap(w,ht)) using(var g=System.Drawing.Graphics.FromImage(bmp)){
      g.CopyFromScreen(x,y,0,0,new System.Drawing.Size(w,ht)); bmp.Save(path,System.Drawing.Imaging.ImageFormat.Png); }
  }
}
"@ -ReferencedAssemblies 'System.Drawing','System.Windows.Forms'
