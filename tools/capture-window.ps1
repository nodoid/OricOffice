# Capture ONLY the oricutron window to a PNG for visual inspection.
param([Parameter(Mandatory = $true)][string]$OutPng)
$ErrorActionPreference = 'Stop'
$p = Get-Process oricutron -ErrorAction Stop
if ($p.MainWindowHandle -eq 0) { throw 'no window' }

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class W32Cap {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
[void][W32Cap]::SetForegroundWindow($p.MainWindowHandle)
Start-Sleep -Milliseconds 500
$r = New-Object W32Cap+RECT
[void][W32Cap]::GetWindowRect($p.MainWindowHandle, [ref]$r)
$w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $h)))
$full = [System.IO.Path]::GetFullPath($OutPng)
$bmp.Save($full, [System.Drawing.Imaging.ImageFormat]::Png)
"saved $full"
$g.Dispose(); $bmp.Dispose()
"captured ${w}x${h} -> $OutPng"
