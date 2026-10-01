# SAM 魔改版调试辅助：枚举窗口控件、读文字、发点击（仅用于自动化验证）
# 用法: . .\ui.ps1  然后调用 Get-ChildWindows / Get-WindowText / Invoke-Click

Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies 'System.Drawing','System.Windows.Forms' -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public class UiProbe
{
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder buf, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder buf, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wp, StringBuilder lp);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    public static List<IntPtr> Children(IntPtr parent)
    {
        var list = new List<IntPtr>();
        EnumChildWindows(parent, (h, l) => { list.Add(h); return true; }, IntPtr.Zero);
        return list;
    }

    public static List<IntPtr> TopLevel(uint pid)
    {
        var list = new List<IntPtr>();
        EnumWindows((h, l) =>
        {
            uint p;
            GetWindowThreadProcessId(h, out p);
            if (p == pid) { list.Add(h); }
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static string ClassOf(IntPtr h)
    {
        var sb = new StringBuilder(256);
        GetClassName(h, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string TextOf(IntPtr h)
    {
        var sb = new StringBuilder(1024);
        GetWindowText(h, sb, sb.Capacity);
        if (sb.Length == 0)
        {
            sb = new StringBuilder(4096);
            SendMessage(h, 0x000D, new IntPtr(sb.Capacity), sb); // WM_GETTEXT
        }
        return sb.ToString();
    }

    public static int ListViewCount(IntPtr h)
    {
        return (int)SendMessage(h, 0x1004, IntPtr.Zero, IntPtr.Zero); // LVM_GETITEMCOUNT
    }

    public static string ListViewSubItem(IntPtr h, int item, int sub)
    {
        var buf = new StringBuilder(512);
        var lvi = new LVITEM();
        lvi.mask = 0x1; // LVIF_TEXT
        lvi.iItem = item;
        lvi.iSubItem = sub;
        lvi.pszText = Marshal.AllocHGlobal(1024);
        lvi.cchTextMax = 512;
        IntPtr p = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(LVITEM)));
        Marshal.StructureToPtr(lvi, p, false);
        SendMessage(h, 0x107D, new IntPtr(item), p); // LVM_GETITEMTEXTW
        string result = Marshal.PtrToStringUni(lvi.pszText) ?? "";
        Marshal.FreeHGlobal(lvi.pszText);
        Marshal.FreeHGlobal(p);
        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LVITEM
    {
        public uint mask;
        public int iItem;
        public int iSubItem;
        public uint state;
        public uint stateMask;
        public IntPtr pszText;
        public int cchTextMax;
        public int iImage;
        public IntPtr lParam;
        public int iIndent;
        public int iGroupId;
        public uint cColumns;
        public IntPtr puColumns;
        public IntPtr piColFmt;
        public int iGroup;
    }

    public static void Click(IntPtr h)
    {
        var r = new RECT();
        GetWindowRect(h, out r);
        int x = (r.Left + r.Right) / 2;
        int y = (r.Top + r.Bottom) / 2;
        SetForegroundWindow(h);
        System.Threading.Thread.Sleep(120);
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(120);
        mouse_event(0x0002, 0, 0, 0, IntPtr.Zero); // LEFTDOWN
        System.Threading.Thread.Sleep(60);
        mouse_event(0x0004, 0, 0, 0, IntPtr.Zero); // LEFTUP
    }

    public static void SaveWindowPng(IntPtr h, string path)
    {
        var r = new RECT();
        GetWindowRect(h, out r);
        int w = r.Right - r.Left, ht = r.Bottom - r.Top;
        using (var bmp = new System.Drawing.Bitmap(w, ht))
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(r.Left, r.Top, 0, 0, new System.Drawing.Size(w, ht));
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
    }
}
"@
