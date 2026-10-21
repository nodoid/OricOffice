# Drives a persistent Oricutron emulator instance across invocations.
#   .\oricutron.ps1 -Start [-Tape <file.tap>] [-ExtraArgs <string>]
#   .\oricutron.ps1 -Keys 'text' [-Return] [-Wait <seconds>]
#   .\oricutron.ps1 -FKey 11 [-Wait 1]     # F11 copies the emulated text screen to the clipboard
#   .\oricutron.ps1 -Screen                # F11 then print the emulated screen
#   .\oricutron.ps1 -Stop
param(
    [switch]$Start,
    [switch]$Keys,
    [int]$FKey = 0,
    [switch]$Screen,
    [switch]$Stop,
    [switch]$Return,
    [string]$Text = '',
    [double]$Wait = 1.5,
    [string]$Tape = '',
    [string]$ExtraArgs = '',
    [string]$Exe = "$env:USERPROFILE\Downloads\Oricutron_win32_v12\oricutron.exe"
)
$ErrorActionPreference = 'Stop'
$procName = 'oricutron'

if ($Stop) {
    Get-Process $procName -ErrorAction SilentlyContinue | Stop-Process -Force
    'stopped'
    return
}

if ($Start) {
    Get-Process $procName -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 300
    $emuDir = Split-Path -Parent (Resolve-Path $Exe).Path
    $argList = @('-w', '-m', 'a')
    if ($Tape) { $argList += @('-t', (Resolve-Path $Tape).Path) }
    if ($ExtraArgs) { $argList += $ExtraArgs }
    Start-Process -FilePath $Exe -ArgumentList $argList -WorkingDirectory $emuDir
    Start-Sleep -Seconds 4
    $p = Get-Process $procName -ErrorAction Stop
    "started PID $($p.Id) window=$($p.MainWindowHandle) title='$($p.MainWindowTitle)'"
    return
}

$p = Get-Process $procName -ErrorAction Stop
if ($p.MainWindowHandle -eq 0) { throw 'oricutron has no window' }

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class OricFocus {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    public const byte VK_RETURN = 0x0D;
    public const uint KEYEVENTF_KEYUP = 0x0002;
}
"@

# Sends keys with explicit down/up timing — the emulated Oric scans its
# keyboard matrix at 50Hz, so ultra-short synthetic presses can be missed.
# Letters go as their uppercase VK (A-Z == their VK codes); digits/space and
# common OEM punctuation are mapped to real VK codes. {ENTER}/{ESC} tokens
# send RETURN and ESCAPE.
$script:VkMap = @{
    ' ' = 0x20; ',' = 0xBC; '.' = 0xBE; '/' = 0xBF; ';' = 0xBA
    "'" = 0xDE; '[' = 0xDB; ']' = 0xDD; '-' = 0xBD; '=' = 0xBB
    '\' = 0xDC; '`' = 0xC0
}
# Characters that need SHIFT held: char -> base VK.
$script:ShiftMap = @{
    '"' = 0xDE; ':' = 0xBA; '?' = 0xBF; '<' = 0xBC; '>' = 0xBE
    '_' = 0xBD; '+' = 0xBB; '~' = 0xC0; '!' = 0x31; '@' = 0x32
    '#' = 0x33; '$' = 0x34; '%' = 0x35; '^' = 0x36; '&' = 0x37
    '*' = 0x38; '(' = 0x39; ')' = 0x30; '{' = 0xDB; '}' = 0xDD
}

function Send-OricKeys([string]$s) {
    [void][OricFocus]::SetForegroundWindow($p.MainWindowHandle)
    Start-Sleep -Milliseconds 400
    $i = 0
    while ($i -lt $s.Length) {
        if ($s[$i] -eq '{') {
            $end = $s.IndexOf('}', $i)
            if ($end -gt $i) {
                $token = $s.Substring($i + 1, $end - $i - 1).ToUpperInvariant()
                if ($token.Length -eq 2 -and $token[0] -eq '^') {
                    # Ctrl+letter: hold Control while tapping the letter VK.
                    $vk = [byte][char]$token[1]
                    [void][OricFocus]::keybd_event(0x11, 0, 0, [UIntPtr]::Zero)
                    Start-Sleep -Milliseconds 60
                    [void][OricFocus]::keybd_event($vk, 0, 0, [UIntPtr]::Zero)
                    Start-Sleep -Milliseconds 90
                    [void][OricFocus]::keybd_event($vk, 0, [OricFocus]::KEYEVENTF_KEYUP, [UIntPtr]::Zero)
                    Start-Sleep -Milliseconds 60
                    [void][OricFocus]::keybd_event(0x11, 0, [OricFocus]::KEYEVENTF_KEYUP, [UIntPtr]::Zero)
                    Start-Sleep -Milliseconds 120
                    $i = $end + 1
                    continue
                }
                $vk = switch ($token) {
                    'ENTER' { 0x0D } 'RETURN' { 0x0D } 'ESC' { 0x1B }
                    default { throw "unknown token {$token}" }
                }
                [void][OricFocus]::keybd_event([byte]$vk, 0, 0, [UIntPtr]::Zero)
                Start-Sleep -Milliseconds 90
                [void][OricFocus]::keybd_event([byte]$vk, 0, [OricFocus]::KEYEVENTF_KEYUP, [UIntPtr]::Zero)
                Start-Sleep -Milliseconds 120
                $i = $end + 1
                continue
            }
        }
        $ch = $s[$i]
        $shift = $false
        $vk = if ($ch -match '[a-zA-Z]') {
            [byte][char][string]([char]::ToUpperInvariant($ch))
        } elseif ($ch -match '[0-9 ]') {
            [byte][char]$ch
        } elseif ($script:VkMap.ContainsKey([string]$ch)) {
            [byte]$script:VkMap[[string]$ch]
        } elseif ($script:ShiftMap.ContainsKey([string]$ch)) {
            $shift = $true
            [byte]$script:ShiftMap[[string]$ch]
        } else {
            throw "no VK mapping for '$ch' - add one or send it another way"
        }
        if ($shift) {
            [void][OricFocus]::keybd_event(0x10, 0, 0, [UIntPtr]::Zero)
            Start-Sleep -Milliseconds 60
        }
        [void][OricFocus]::keybd_event($vk, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 90
        [void][OricFocus]::keybd_event($vk, 0, [OricFocus]::KEYEVENTF_KEYUP, [UIntPtr]::Zero)
        if ($shift) {
            Start-Sleep -Milliseconds 60
            [void][OricFocus]::keybd_event(0x10, 0, [OricFocus]::KEYEVENTF_KEYUP, [UIntPtr]::Zero)
        }
        Start-Sleep -Milliseconds 120
        $i++
    }
}

if ($Keys) {
    if (-not $Text) { throw '-Keys requires -Text' }
    Send-OricKeys $Text
    if ($Return) { Send-OricKeys '{ENTER}' }
    Start-Sleep -Seconds $Wait
    "sent ($Text)$(if ($Return) {' + RETURN'})"
    return
}

if ($FKey -gt 0) {
    # Function keys go through SendKeys (VK codes F1-F24 exceed the byte range
    # keybd_event takes for letters).
    $wsh = New-Object -ComObject WScript.Shell
    [void][OricFocus]::SetForegroundWindow($p.MainWindowHandle)
    Start-Sleep -Milliseconds 400
    $wsh.SendKeys("{F$FKey}")
    Start-Sleep -Seconds $Wait
    return
}

if ($Screen) {
    $wsh = New-Object -ComObject WScript.Shell
    [void][OricFocus]::SetForegroundWindow($p.MainWindowHandle)
    Start-Sleep -Milliseconds 400
    $wsh.SendKeys('{F11}')
    Start-Sleep -Seconds 1.5
    Get-Clipboard -Raw
    return
}
