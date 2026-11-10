param(
    [string] $ProcessName = "MediaMTX-Yuki-GUI",
    [Parameter(Mandatory = $true)][string] $TitleLike,
    [int] $ClickX = -1,
    [int] $ClickY = -1,
    [string] $Keys = "",
    [string] $Out = "dialog.png",
    [int] $DelayMs = 900
)

# 截「同进程里不是主窗口」的那个顶层窗口——编辑窗是独立 HWND，capture_screen.ps1 抓不到它。
# 不用 FindWindow：Avalonia 窗口标题在 Win32 侧不稳定，按标题找经常落空。
# 保险做法是把这个进程所有可见顶层窗口数出来、再按标题包含匹配挑一个；落空时把候选全列出来。
#
# Order matters: declare P/Invoke and set DPI awareness BEFORE loading WinForms/Drawing,
# otherwise the DPI context is already fixed and SetCursorPos scales the coordinates a second time.

$signature = @'
using System;
using System.Text;
using System.Runtime.InteropServices;

public static class Win32Dialog
{
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

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
    public static extern bool EnumWindows(EnumProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern int GetWindowThreadProcessId(IntPtr hWnd, out int pid);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder buffer, int maxCount);

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


# ====== DPI ======

$dpiOk = [Win32Dialog]::SetProcessDpiAwarenessContext([IntPtr](-4))  # -4 = PER_MONITOR_AWARE_V2

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing


# ====== 找窗口 ======

$process = Get-Process -Name $ProcessName -ErrorAction Stop | Select-Object -First 1
$targetPid = $process.Id

$script:windows = New-Object System.Collections.ArrayList

$callback = [Win32Dialog+EnumProc]{
    param($hwnd, $lParam)

    $ownerPid = 0
    [Win32Dialog]::GetWindowThreadProcessId($hwnd, [ref] $ownerPid) | Out-Null
    if ($ownerPid -ne $targetPid) { return $true }
    if (-not [Win32Dialog]::IsWindowVisible($hwnd)) { return $true }

    $length = [Win32Dialog]::GetWindowTextLength($hwnd)
    $buffer = New-Object System.Text.StringBuilder ($length + 2)
    [Win32Dialog]::GetWindowText($hwnd, $buffer, $buffer.Capacity) | Out-Null
    [void] $script:windows.Add([pscustomobject]@{ Handle = $hwnd; Title = $buffer.ToString() })

    return $true
}

[Win32Dialog]::EnumWindows($callback, [IntPtr]::Zero) | Out-Null

$target = $null
foreach ($window in $windows)
    {
    if ($window.Title -like "*$TitleLike*") { $target = $window; break }
    }

if (-not $target)
    {
    foreach ($window in $windows) { Write-Output "cand: [$($window.Title)]" }
    Write-Output "no window matched title=$TitleLike"
    exit 1
    }

[Win32Dialog]::SetForegroundWindow($target.Handle) | Out-Null
Start-Sleep -Milliseconds 400

$rect = New-Object Win32Dialog+RECT
[Win32Dialog]::GetWindowRect($target.Handle, [ref] $rect) | Out-Null


# ====== 可选：点一下（窗内相对坐标） ======

$clickNote = ""
if ($ClickX -ge 0 -and $ClickY -ge 0)
    {
    $targetX = $rect.Left + $ClickX
    $targetY = $rect.Top + $ClickY

    [Win32Dialog]::SetCursorPos($targetX, $targetY) | Out-Null
    Start-Sleep -Milliseconds 150

    $actual = New-Object Win32Dialog+POINT
    [Win32Dialog]::GetCursorPos([ref] $actual) | Out-Null
    if ($actual.X -ne $targetX -or $actual.Y -ne $targetY)
        {
        $clickNote = " CURSOR-MISS actual=($($actual.X),$($actual.Y))"
        }

    [Win32Dialog]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [Win32Dialog]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 300

    $clickNote = "$clickNote click=($targetX,$targetY)"
    }


# ====== 可选：发键（竖线分隔多串） ======

if ($Keys -ne "")
    {
    foreach ($item in $Keys.Split("|"))
        {
        [System.Windows.Forms.SendKeys]::SendWait($item)
        Start-Sleep -Milliseconds 200
        }
    }

Start-Sleep -Milliseconds $DelayMs


# ====== 截图 ======

$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top

$bitmap = New-Object System.Drawing.Bitmap($width, $height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, $bitmap.Size)
$graphics.Dispose()

$path = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Out))
$bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()

$sizeNote = "$($width)x$($height)"
Write-Output "dpiAware=$dpiOk window=[$($target.Title)] at=($($rect.Left),$($rect.Top)) $sizeNote$clickNote saved=$Out"
