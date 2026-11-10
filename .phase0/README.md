# `.phase0` — 迁移验证工装

> 这个目录**不是产品代码**，是 WPF → Avalonia 迁移期间的验证工具，保留作可复用资产。
> 整个目录可以删掉而不影响 `src/`。

## 脚本清单

| 脚本 | 用途 |
| --- | --- |
| `capture_screen.ps1` | **推荐**：按进程名 + `SetForegroundWindow` + 睡 700ms + `CopyFromScreen`，抓屏幕真实内容 |
| `click_capture.ps1` | **推荐**：窗口内相对坐标点击 → 截图，带 DPI 与落点校验（`CURSOR-MISS` 自检） |
| `sendkey.ps1` | 可选先点击激活，再 `SendKeys` 发键（回车提交 / ESC 取消），然后截图 |
| `dialog_probe.ps1` | **推荐**：抓「同进程的非主窗口」（编辑窗是独立 HWND），可先点一下、再发键，最后截图 |

## ⚠️ 踩过的坑（复用前必读）

### 1. DPI 感知必须在任何 UI 库加载之前确立

**症状**：点击脚本点位总往右下飘——点「目标」切到「日志」、点铅笔按钮没反应。

**根因**：脚本先 `Add-Type -AssemblyName System.Windows.Forms`，加载 WinForms 的那一刻进程 DPI 上下文就定型成「不感知」；之后再调 `SetProcessDPIAware()` 已晚，系统静默返回 `false`，而 `| Out-Null` 把失败吞了。于是 `SetCursorPos` 的坐标被系统**再乘一次 1.25**（125% 缩放）。

**正确顺序**：

```powershell
Add-Type -TypeDefinition $signature          # 只声明 P/Invoke
[Win32Click]::SetProcessDpiAwarenessContext([IntPtr](-4))   # PerMonitorV2，立刻
Add-Type -AssemblyName System.Drawing        # 之后才加载 UI/绘图库
```

并且**回读校验**：`SetCursorPos` 之后用 `GetCursorPos` 确认落点，不一致就报 `CURSOR-MISS`。`click_capture.ps1` 已内置这两点，输出形如：

```
dpiAware=True window=(546,148) 1468x1084 click=(591,376) saved=probe.png
```

> 顺带：`SetProcessDpiAwarenessContext(-4)` 在 PowerShell 7 里常常回 `False`（进程启动时就已经定过上下文），
> **这不代表坐标会飘**——`dialog_probe.ps1` 实测 `dpiAware=False` 但点击与窗口尺寸都是物理像素、点得准。
> 判断感知是否生效看结果（点位对不对、`1468x1084` 这种物理尺寸对不对），别只看那个布尔。

### 2. `PrintWindow` 会返回旧的位图缓存

`PrintWindow(hwnd, hdc, 2)` 拿到的可能是**上一帧**的内容：改了 XAML 重新编译启动后，新旧两张截图的 MD5 可能完全一样，导致误判「改动没生效」。

**验证 UI 改动请用 `capture_screen.ps1`（屏幕抓取），不要用 `PrintWindow`。**
（原先的 `capture_any.ps1` / `capture_pw.ps1` 就是这条路的，已删。）

### 3. 截图左上角那圈不属于窗口

`GetWindowRect` 包含 DWM 的阴影区，截图左边 0–9px、上方约 32px 是桌面与标题栏。判读颜色/坐标时要跳过这段，否则会把桌面壁纸的蓝色当成 UI 元素（本鱼在这上面误判过两次）。

### 4. 窗口内相对坐标速查（主窗 1468×1084 物理，125% 缩放）

| 目标 | 坐标 (x, y) |
| --- | --- |
| 侧栏 总览 | (45, 78) |
| 侧栏 输入源 | (45, 165) |
| 侧栏 目标 | (45, 228) |
| 侧栏 日志 | (45, 300) |
| 侧栏 设置 | (45, 1012) |
| 设置页第一组标题 | (200, 248) |
| 页面右上「添加输入 / 添加目标」 | (1380, 174) |
| 编辑窗「添加 / 保存」 | (867, 796) |
| 编辑窗「取消」 | (966, 796) |

> 导航项是 56×52 逻辑（70×65 物理），中心间隔约 65px；以上是实测值，换了缩放比例要重算。

### 5. 编译前先杀进程

不 `taskkill //F //IM MediaMTX-Yuki-GUI.exe` 就 `dotnet build`，会报 `MSB3027 / MSB3021`（exe 被占用）。

### 6. 编辑窗是独立窗口，主窗截图里没有它

输入源 / 输出目标的编辑窗都是**另一个顶层 HWND**，`capture_screen.ps1` 与 `click_capture.ps1` 都只抓进程主窗口，弹窗一出现，截到的还是被挡在后面那个主窗。

两条路都踩过：

- `FindWindow` / `Get-Process MainWindowHandle` 按标题找——**找不到**。Avalonia 的窗口标题在 Win32 侧不稳定，`MainWindowHandle` 永远指主窗。
- 正解是 `EnumWindows` 枚举该进程全部可见顶层窗口，逐个 `GetWindowText` 取标题，再按「标题包含」挑。`dialog_probe.ps1` 就是这么做的，落空时会把候选全列出来（`cand: [...]`），不会静默失败。

```powershell
# Out 相对当前目录，先 Set-Location 到要存的目录
& .\dialog_probe.ps1 -TitleLike "输入源" -Out edit.png

# 点一下再发键：两个动作在同一个脚本里连着做，省得中间弹窗焦点乱跑
& .\dialog_probe.ps1 -TitleLike "添加输入源" -ClickX 313 -ClickY 445 -Keys "^a{DEL}" -Out cleared.png
```

参数：`-ProcessName`（默认 `MediaMTX-Yuki-GUI`）、`-TitleLike`（必填，标题包含即中）、`-ClickX` / `-ClickY`（窗内相对坐标，负数表示不点）、`-Keys`（`SendKeys` 串，多个用 `|` 分隔）、`-Out`（相对当前目录）、`-DelayMs`（动作后等多久再截，默认 900）。

### 7. `SendKeys` 发英文会被输入法吃掉

中文输入法开着的时候，`SendKeys("live")` 会把字符送进候选窗、正文一个字都没有。两条对策：

- 只保证「清空」这类动作时用 `^a{DEL}`，不依赖输入内容；
- 真要打字，先 `Set-Clipboard` 再 `SendKeys("^v")` 粘贴。

### 8. ComboBox 展开后的下拉是独立 HWND，截不到

点开下拉再截主窗（或截编辑窗）都**只有收起状态的框**，看不到选项列表；用 `{DOWN}` + `{ENTER}` 遥控也不可靠。

**要验「换档位之后的效果」，直接改 `bin\Debug\net10.0\config\app.json` 里那个字段再重启**，比跟下拉较劲快得多。

## 脚本约定

- **行尾 LF、缩进 4 空格**：本目录已随整个项目统一（见项目根 `.editorconfig`），改脚本时不用再判断。
- **编码必须带 UTF-8 BOM**：Windows PowerShell 5.1 读无 BOM 的 UTF-8 会按 ANSI 解，脚本里的中文提示直接变乱码。
  新建脚本后如果是无 BOM 的，用 `Set-Content -Encoding utf8BOM` 或编辑器另存为「带 BOM」补上。
- 改完脚本先做语法检查再跑：`[System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$null, [ref]$errors)`。

## 探针工程

| 文件 | 说明 |
| --- | --- |
| `Phase0.csproj` | 独立工程，`net10.0` + Avalonia 12.1.5 + FluentAvaloniaUI 3.1.0 |
| `Program.cs` | `[STAThread] Main` → `BuildAvaloniaApp().StartWithClassicDesktopLifetime(args)` |
| `App.axaml` | `<FluentTheme />` + `<fa:FluentAvaloniaTheme />` |
| `MainWindow.axaml` | 五栏堆叠手风琴：左列第 1 栏 `Height` 0↔280 循环，右列固定展开作静止对照 |

跑法：

```powershell
cd "F:\Projects\MediaMTX-Yuki-GUI for Windows\.phase0"
dotnet run
```

窗口会置顶并自动循环。盯住左列第 2~5 个标题，来回看笔画是否随动画发虚——
发虚就是 WPF 那种病又回来了（正常情况：整段动画期间笔画都是实的）。

## 结论在哪

迁移本身的动机、方案与验收记录已并入 [`../docs/交接文档.md`](../docs/交接文档.md)
（§一「为什么从 WPF 迁到 Avalonia」、§九「验证工装」）。
