<#
  用 UIA 驱动界面做验证：按无障碍名称找控件，避开坐标漂移与中文编码问题。
  用法：
    .\ui_probe.ps1 -Action list
    .\ui_probe.ps1 -Action select -Name 目标
    .\ui_probe.ps1 -Action click -Name 添加目标
    .\ui_probe.ps1 -Action combo -ComboIndex 4 -Item 自定义代理
    .\ui_probe.ps1 -Action shot -Out x.png
    .\ui_probe.ps1 -Action input -InputIndex 3 -Text "rtmp://"
#>
param(
  [Parameter(Mandatory = $true)][string]$Action,
  [string]$Name = '',
  [string]$Item = '',
  [int]$ComboIndex = -1,
  [int]$InputIndex = -1,
  [string]$Text = '',
  [string]$Out = 'shot.png',
  [string]$ProcessName = 'MediaMTX-Yuki-GUI'
)

# 输出统一走 UTF-8，避免中文在管道里被按 GBK 解码成乱码
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process $ProcessName -ErrorAction Stop | Select-Object -First 1
$rootElement = [System.Windows.Automation.AutomationElement]::RootElement
$byPid = [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
$windows = $rootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $byPid)

$mainWindow = $null
$dialog = $null
foreach ($w in $windows) {
  $rect = $w.Current.BoundingRectangle
  if ($w.Current.ClassName -eq 'MainWindow') {
    $mainWindow = $w
  }
  elseif ($rect.Width -gt 300 -and $rect.Height -gt 200) {
    $dialog = $w
  }
}
if ($null -eq $mainWindow) { throw '找不到主窗口' }

$scope = $mainWindow
if ($null -ne $dialog) { $scope = $dialog }

$all = $scope.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

switch ($Action) {
  'list' {
    Write-Output ('scope=' + $scope.Current.ClassName + ' count=' + $all.Count)
    foreach ($e in $all) {
      $typeName = $e.Current.ControlType.ProgrammaticName -replace 'ControlType\.', ''
      $label = $e.Current.Name
      if ([string]::IsNullOrWhiteSpace($label)) { continue }
      if ($typeName -in @('Text', 'Image', 'Pane')) { continue }
      $box = $e.Current.BoundingRectangle
      Write-Output ($typeName.PadRight(12) + ' | ' + $label + ' | ' + [int]$box.X + ',' + [int]$box.Y + ' ' + [int]$box.Width + 'x' + [int]$box.Height)
    }
  }

  'select' {
    $target = $null
    foreach ($e in $all) {
      if ($e.Current.Name -eq $Name -and $e.Current.ControlType.ProgrammaticName -eq 'ControlType.RadioButton') { $target = $e }
    }
    if ($null -eq $target) { throw ('找不到导航项：' + $Name) }
    $target.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 800
    Write-Output ('selected ' + $Name)
  }

  'click' {
    $target = $null
    foreach ($e in $all) {
      if ($e.Current.Name -eq $Name -and $e.Current.ControlType.ProgrammaticName -eq 'ControlType.Button') { $target = $e }
    }
    if ($null -eq $target) { throw ('找不到按钮：' + $Name) }
    $target.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 1300
    Write-Output ('clicked ' + $Name)
  }

  'combo' {
    $combos = @()
    foreach ($e in $all) {
      if ($e.Current.ControlType.ProgrammaticName -eq 'ControlType.ComboBox') { $combos += $e }
    }
    if ($ComboIndex -lt 0 -or $ComboIndex -ge $combos.Count) {
      Write-Output ('lives ComboBox count=' + $combos.Count)
      for ($i = 0; $i -lt $combos.Count; $i++) {
        $box = $combos[$i].Current.BoundingRectangle
        Write-Output ('  #' + $i + ' value=' + $combos[$i].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value + ' ' + [int]$box.X + ',' + [int]$box.Y)
      }
      throw 'ComboBox 索引超范围'
    }
    $combo = $combos[$ComboIndex]
    $expand = $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expand.Expand()
    Start-Sleep -Milliseconds 700
    $allItems = $scope.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    $hit = $null
    foreach ($e in $allItems) {
      if ($e.Current.Name -eq $Item) { $hit = $e }
    }
    if ($null -eq $hit) { throw ('下拉里找不到：' + $Item) }

    # Avalonia 的下拉项常常只暴露成文本元素，没有 SelectionItemPattern。
    # 这时退回鼠标点击：取元素矩形中心，点到它身上。
    $hasSelection = $false
    try {
      $probe = $null
      if ($hit.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$probe)) { $hasSelection = $true }
    }
    catch { $hasSelection = $false }

    if ($hasSelection) {
      $hit.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    }
    else {
      Add-Type @'
using System;
using System.Runtime.InteropServices;
public class UiMouse {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flag, uint dx, uint dy, uint data, int extra);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
'@
      [UiMouse]::SetProcessDPIAware() | Out-Null
      $box = $hit.Current.BoundingRectangle
      $px = [int]($box.X + $box.Width / 2)
      $py = [int]($box.Y + $box.Height / 2)
      [UiMouse]::SetCursorPos($px, $py) | Out-Null
      Start-Sleep -Milliseconds 250
      [UiMouse]::mouse_event(0x0002, 0, 0, 0, 0)
      Start-Sleep -Milliseconds 60
      [UiMouse]::mouse_event(0x0004, 0, 0, 0, 0)
    }

    Start-Sleep -Milliseconds 900
    Write-Output ('selected item ' + $Item + ' (pattern=' + $hasSelection + ')')
  }

  'input' {
    $edits = @()
    foreach ($e in $all) {
      if ($e.Current.ControlType.ProgrammaticName -eq 'ControlType.Edit') { $edits += $e }
    }
    if ($InputIndex -lt 0 -or $InputIndex -ge $edits.Count) {
      Write-Output ('lives Edit count=' + $edits.Count)
      for ($i = 0; $i -lt $edits.Count; $i++) {
        $box = $edits[$i].Current.BoundingRectangle
        Write-Output ('  #' + $i + ' ' + [int]$box.X + ',' + [int]$box.Y + ' ' + [int]$box.Width + 'x' + [int]$box.Height)
      }
      throw 'Edit 索引超范围'
    }
    $edit = $edits[$InputIndex]
    $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Text)
    Start-Sleep -Milliseconds 400
    Write-Output ('typed into #' + $InputIndex)
  }

  'shot' {
    $target = $scope
    $box = $target.Current.BoundingRectangle
    $width = [int]$box.Width
    $height = [int]$box.Height
    $bmp = New-Object System.Drawing.Bitmap($width, $height)
    $gfx = [System.Drawing.Graphics]::FromImage($bmp)
    $gfx.CopyFromScreen([int]$box.X, [int]$box.Y, 0, 0, $bmp.Size)
    $bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
    $gfx.Dispose()
    $bmp.Dispose()
    Write-Output ('saved ' + $Out + ' ' + $width + 'x' + $height)
  }

  default { throw ('未知动作：' + $Action) }
}
