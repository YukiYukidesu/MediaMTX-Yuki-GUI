param(
    [Parameter(Mandatory = $true)][string]$ProcessName,
    [string]$Key = "{ENTER}",
    [string]$Out = "sendkey.png",
    [int]$ClickX = -1,
    [int]$ClickY = -1,
    [int]$DelayMs = 1200
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32Key {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

[Win32Key]::SetProcessDPIAware() | Out-Null

$proc = Get-Process -Name $ProcessName -ErrorAction Stop |
    Where-Object { $_.MainWindowHandle -ne 0 } |
    Select-Object -First 1

if (-not $proc) {
    Write-Error "no window for $ProcessName"
    exit 1
}

$rect = New-Object Win32Key+RECT
[Win32Key]::GetWindowRect($proc.MainWindowHandle, [ref]$rect) | Out-Null

[Win32Key]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 300

if ($ClickX -ge 0 -and $ClickY -ge 0) {
    [Win32Key]::SetCursorPos($rect.Left + $ClickX, $rect.Top + $ClickY) | Out-Null
    Start-Sleep -Milliseconds 120
    [Win32Key]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    [Win32Key]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 350
}

[System.Windows.Forms.SendKeys]::SendWait($Key)
Start-Sleep -Milliseconds $DelayMs

$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top

$bmp = New-Object System.Drawing.Bitmap $width, $height
$gfx = [System.Drawing.Graphics]::FromImage($bmp)
$gfx.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bmp.Size)
$bmp.Save((Join-Path (Get-Location) $Out), [System.Drawing.Imaging.ImageFormat]::Png)
$gfx.Dispose()
$bmp.Dispose()

Write-Output "sent $Key, saved $Out at ${width}x${height}"
