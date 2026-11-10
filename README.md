# MediaMTX-Yuki-GUI （这版先让大肥鱼代笔下，仅测试，之后作者再重写）

MediaMTX 的 Windows 桌面外壳：把一台机器上的推流转发、录制、鉴权、状态监控收进一个窗口里。

MediaMTX 本身是命令行程序，配置文件是一大坨 YAML。日常要干的活其实就那么几件——加一路推流入口、把它转发到几个平台、看现在有没有在跑、谁推得进来——但每件都得改配置、重启、翻日志。这个程序把这些包了一层，改完点保存就行，引擎那边由它自己重启。

> 版本 v0.0.1，还很早期。功能是齐的，界面与交互仍在按实际使用习惯调整。

## 它长什么样

窗口左侧一列导航，右侧是对应页面：

| 页面 | 干什么 |
| --- | --- |
| 总览 | 引擎现在跑没跑，一路输入源分发给了哪些输出目标，一眼看完 |
| 输入源 | 建立各路推流入口：接收推流（OBS 之类推上来）或主动拉流（引擎去连远端地址） |
| 输出目标 | 把某一路输入转发到各平台，可独立启停 |
| 账号 | 一张账号表管所有推流身份，每个输入源单独勾选「允许谁往这一路推」 |
| 统计数据 | 入流与各路的实时码率、已发送量，每 2 秒刷新 |
| 运行日志 | 引擎与界面自己的标准输出，可一键复制或导出 |
| 设置 | 引擎参数、协议开关、录制、目录、更新与代理 |
| 工作模式 | 简洁模式与高级模式的切换 |

两种模式是给不同人用的：简洁模式只留日常转流要用的东西，引擎里也只开最小的一套协议；高级模式把拉流、录制、鉴权、连接治理这些放出来。**切回简洁模式不会改掉你在高级模式里配过的东西**，只是暂时不生效。

## 怎么跑起来

### 要求

- Windows 10 / 11（x64）
- [.NET 10 SDK](https://dotnet.microsoft.com/download)（构建用；跑发布包不需要）

### 从源码构建

```powershell
dotnet build 'src\MediaMTX-Yuki-GUI\MediaMTX-Yuki-GUI.csproj' -c Debug
dotnet run --project 'src\MediaMTX-Yuki-GUI\MediaMTX-Yuki-GUI.csproj'
```

### 打包成发布包

```powershell
.\build-release.ps1              # 两个包都出
.\build-release.ps1 -NoRestore   # 依赖没变时跳过 restore
```

产出两种包，都在 `dist\`：

- `-full.zip`：自带 .NET 运行时，目标机什么都不用装
- `-light.zip`：外挂运行时，目标机要先装 [.NET 10 运行时](https://dotnet.microsoft.com/download/dotnet/10.0)（Runtime，不是 Desktop Runtime）

程序本身的代码全部焊进单文件 exe，不留散落的 dll。

### 关于引擎

**仓库里不带 MediaMTX 引擎**（它有自己的发布节奏与许可证）。第一次启动时如果没有检测到引擎，界面右下角会提示，点「前往下载」到设置页取一份即可，装完自动接上。

引擎装在程序目录下的 `MediaMTX\`，录制文件落在 `recordings\`，日志在 `logs\`。

## 目录结构

```
src/MediaMTX-Yuki-GUI/
  Controls/     自绘控件：滚动条联动、Toast 气泡、气泡确认、平滑展开、滚轮缓动
  Models/       配置模型，直接对应 app.json 的结构
  Services/     引擎管理、配置读写与迁移、YAML 生成、Control API、更新下载、代理
  ViewModels/   每个页面一个部分类，外壳状态在 MainViewModel.cs
  Views/        八个页面与几个模态编辑窗
  Resources/    设计系统：颜色、字号、圆角、控件主题
  Languages/    界面文案，加语言不用改代码
```

配置落在 `config\app.json`，运行时生成的引擎配置在 `config\runtime.yml`。前者是给人看的、可以手改；后者每次启动重写。

## 加一种语言

往 `src\MediaMTX-Yuki-GUI\Languages\` 丢一个 `xx-YY.json`（BCP 47 标签），程序启动时会自动扫到它。格式与约定见该目录下的 [README](src/MediaMTX-Yuki-GUI/Languages/README.md)。

## 技术栈

- [Avalonia 12](https://avaloniaui.net/) + [FluentAvalonia](https://github.com/amwx/FluentAvalonia)：跨平台 UI，Windows 上走云母 / 亚克力材质
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)：MVVM 源生成器
- Windows 原生 DWM 接口：标题栏、材质、窗口阴影

## 致谢

- [MediaMTX](https://github.com/bluenviron/mediamtx)：本项目只是它的外壳，所有转流能力都来自它
- [Avalonia](https://github.com/AvaloniaUI/Avalonia) 与 [FluentAvalonia](https://github.com/amwx/FluentAvalonia)
- [Filee](https://github.com/okwhm/Filee)：窗口生命周期与几条 UI 手法的参照

## 许可

[MIT](LICENSE)。随便用，改动、闭源、商用都行，只要保留那份版权声明。

（MediaMTX 引擎不随本仓库分发，它有自己的许可证。）
