# Focuses a specific window handle and sends keystrokes to it.
#   .\send-to-window.ps1 -Hwnd 21894850 -Text 'hello.tap' -Enter
param(
    [Parameter(Mandatory = $true)][long]$Hwnd,
    [Parameter(Mandatory = $true)][string]$Text,
    [switch]$Enter,
    [int]$SettleMs = 600
)
$ErrorActionPreference = 'Stop'
$hwndPtr = [IntPtr]$Hwnd
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class WinFocus {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    public const int SW_RESTORE = 9;
}
"@
[void][WinFocus]::ShowWindow($hwndPtr, [WinFocus]::SW_RESTORE)
[void][WinFocus]::SetForegroundWindow($hwndPtr)
[void][WinFocus]::BringWindowToTop($hwndPtr)
Start-Sleep -Milliseconds $SettleMs
$wsh = New-Object -ComObject WScript.Shell
$wsh.SendKeys($Text)
Start-Sleep -Milliseconds 400
if ($Enter) { $wsh.SendKeys('{ENTER}'); Start-Sleep -Milliseconds 400 }
"sent '$Text'$(if ($Enter) {' + ENTER'}) to $hwndPtr"
