# Enumerates the top-level windows of a process (window handle, visibility, title).
param([Parameter(Mandatory = $true)][int]$ProcId)
Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
using System.Collections.Generic;
public class WinEnum {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  public static List<string> List(int pid) {
    var res = new List<string>();
    EnumWindows((h,l) => {
      int p; GetWindowThreadProcessId(h, out p);
      if (p == pid) {
        var sb = new StringBuilder(256);
        GetWindowText(h, sb, 256);
        res.Add(h.ToInt64() + " visible=" + IsWindowVisible(h) + " '" + sb + "'");
      }
      return true;
    }, IntPtr.Zero);
    return res;
  }
}
'@
[WinEnum]::List($ProcId)
