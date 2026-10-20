# Smoke test: launch a built unpackaged MAUI Windows app, verify it stays alive,
# capture ONLY its own window to a PNG for visual inspection, then stop it.
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [Parameter(Mandatory = $true)][string]$ProcName,
    [Parameter(Mandatory = $true)][string]$OutPng,
    [string]$Arguments = ''
)
$ErrorActionPreference = 'Stop'

$exePath = (Resolve-Path $Exe).Path
if ($Arguments) { Start-Process $exePath -ArgumentList $Arguments } else { Start-Process $exePath }
Start-Sleep -Seconds 8

$p = Get-Process $ProcName -ErrorAction Stop
if ($p.MainWindowHandle -eq 0) { throw "$ProcName has no main window" }
"PID: $($p.Id) | Title: '$($p.MainWindowTitle)' | Responding: $($p.Responding)"

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class W32Smoke {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
}
"@
$r = New-Object W32Smoke+RECT
[void][W32Smoke]::GetWindowRect($p.MainWindowHandle, [ref]$r)
$w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
if ($w -le 0 -or $h -le 0) { throw "window rect degenerate: ${w}x${h}" }
"Window: $($r.Left),$($r.Top) ${w}x${h}"

$bmp = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object System.Drawing.Size($w, $h)))
$outDir = Split-Path -Parent $OutPng
if ($outDir -and !(Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }
$bmp.Save((Join-Path (Get-Location) $OutPng), [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()

Stop-Process -Id $p.Id -Force
"OK: $ProcName captured to $OutPng and stopped"
