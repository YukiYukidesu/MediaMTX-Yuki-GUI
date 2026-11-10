param(
    [Parameter(Mandatory = $true)][string] $ProcessName,
    [Parameter(Mandatory = $true)][int] $ClickX,
    [Parameter(Mandatory = $true)][int] $ClickY,
    [Parameter(Mandatory = $true)][string] $Out,
    [int] $DelayMs = 900
)

# Order matters: declare P/Invoke and set DPI awareness BEFORE loading WinForms/Drawing,
# otherwise the DPI context is already fixed and SetCursorPos scales the coordinates a second time.
$signature = @'
using System;
using System.Runtime.InteropServices;

public static class Win32Click
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    public static extern bool SetProcessDpiAwarenessContext(IntPtr value);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
}
'@

Add-Type -TypeDefinition $signature

# -4 = DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
$dpiOk = [Win32Click]::SetProcessDpiAwarenessContext([IntPtr](-4))

Add-Type -AssemblyName System.Drawing

$process = Get-Process -Name $ProcessName -ErrorAction Stop | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
$hwnd = $process.MainWindowHandle

[Win32Click]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Milliseconds 400

$rect = New-Object Win32Click+RECT
[Win32Click]::GetWindowRect($hwnd, [ref] $rect) | Out-Null

$targetX = $rect.Left + $ClickX
$targetY = $rect.Top + $ClickY

[Win32Click]::SetCursorPos($targetX, $targetY) | Out-Null
Start-Sleep -Milliseconds 150

$actual = New-Object Win32Click+POINT
[Win32Click]::GetCursorPos([ref] $actual) | Out-Null
$drift = ""
if ($actual.X -ne $targetX -or $actual.Y -ne $targetY)
{
    $drift = " CURSOR-MISS actual=($($actual.X),$($actual.Y))"
}

[Win32Click]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 60
[Win32Click]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)

Start-Sleep -Milliseconds $DelayMs

$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top

$bitmap = New-Object System.Drawing.Bitmap($width, $height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size)
$graphics.Dispose()

$path = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Out))
$bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()

Write-Output "dpiAware=$dpiOk window=($($rect.Left),$($rect.Top)) ${width}x${height} click=($targetX,$targetY)$drift saved=$Out"
