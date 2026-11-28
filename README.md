# MediaMTX-Yuki-GUI

（这版先让大肥鱼代笔下，仅测试，之后作者再重写）

MediaMTX 的 Windows 桌面外壳：把多路推流转发、录制、鉴权与状态监控收进一个窗口。

MediaMTX 本身是命令行程序，配置是一份 YAML。日常操作只有几件事——新增一路推流入口、把它转发到几个平台、确认当前状态、查看谁在推流——但每一步都要改配置、重启、翻日志。本程序把这些操作封装为界面：修改后保存即可，引擎由程序自己重启。

> 版本 v0.0.5，早期版本。功能完整，界面与交互仍在按实际使用调整。

## 界面

窗口左侧为导航栏，右侧为对应页面：

| 页面 | 用途 |
| --- | --- |
| 总览 | 引擎运行状态，以及每路输入源向哪些输出目标分发 |
| 输入源 | 建立各路推流入口：接收推流（如 OBS）或主动拉流（引擎连接远端地址） |
| 输出目标 | 将某一路输入转发到各平台，可独立启停 |
| 权限账号 | 统一管理推流身份，每个输入源单独授权允许推流的账号 |
| 统计数据 | 入流与各路目标的实时码率、已发送量，每 5 秒刷新 |
| 运行日志 | 引擎与程序的标准输出，支持一键复制或导出 |
| 高级模式 | 简洁模式与高级模式切换 |
| 设置 | 引擎参数、协议开关、录制、目录、更新与代理 |

两种模式面向不同使用强度：简洁模式只保留日常转流所需的项目，引擎侧也只启用最小协议集；高级模式开放拉流、录制、鉴权与连接治理。切回简洁模式不会修改高级模式里已配置的内容，只是暂时不生效。

## 运行

### 环境要求

- Windows 10 / 11（x64）
- [.NET 10 SDK](https://dotnet.microsoft.com/download)（仅构建需要；发布包无需安装 .NET）

### 从源码构建

```powershell
dotnet build 'src\MediaMTX-Yuki-GUI\MediaMTX-Yuki-GUI.csproj' -c Debug
dotnet run --project 'src\MediaMTX-Yuki-GUI\MediaMTX-Yuki-GUI.csproj'
```

### 打包发布包

```powershell
.\build-release.ps1            # 生成两个包
.\build-release.ps1 -Label test  # 包名追加 _test，便于并存
.\build-release.ps1 -SkipEngine  # 两个包都不带引擎
```

打包必须执行 restore，不要加 `-NoRestore`（缺少运行时标识资产会导致 NETSDK1047）。产物在 `dist\`，文件名带版本号（取自 csproj 的 `<Version>`）：

- `MediaMTX-Yuki-GUI_<版本>_x64.zip`：仅程序本体，目标机无需额外安装
- `MediaMTX-Yuki-GUI_<版本>_x64_With_MediaMTX.zip`：额外包含 `MediaMTX\` 目录，内含适配版本的引擎

程序采用自包含、裁剪、单文件打包：代码与 .NET 运行时全部编进一个 exe，不产生散落的 dll。

### 关于引擎

**仓库不包含 MediaMTX 引擎**（引擎有自己的发布节奏与许可证）。打包时按 csproj 的 `<RecommendedEngineVersion>` 下载指定版本，放入 `_x64_With_MediaMTX` 包。

若使用不带引擎的包，或首次启动时未检测到引擎，右下角会给出提示，可前往设置页下载安装，安装完成后自动接入。

引擎安装在程序目录下的 `MediaMTX\`，录制文件写入 `recordings\`，日志写入 `logs\`。

## 目录结构

```
src/MediaMTX-Yuki-GUI/
  Controls/     自绘控件：列表横向平移、平滑滚动、Toast 气泡、气泡确认、平滑展开、关系图
  Models/       配置模型，对应 app.json 结构
  Services/     引擎管理、配置读写与迁移、YAML 生成、Control API、更新下载、代理与本地 RTMP 中转
  ViewModels/   各页面按部分类拆分，外壳状态在 MainViewModel.cs
  Views/        八个页面与若干模态编辑窗口
  Resources/    设计系统：颜色、字号、圆角、控件主题
```

配置位于 `config\app.json`，可手工编辑；引擎运行配置为 `config\runtime.yml`，每次启动重写，勿手工修改。

## 新增语言

在项目根的 `Languages\` 中放入一个 `xx-YY.json`（BCP 47 标签），程序启动时自动扫描。格式与约定见 [Languages\README.md](Languages/README.md)。

## 技术栈

- [Avalonia 12](https://avaloniaui.net/) + [FluentAvalonia](https://github.com/amwx/FluentAvalonia)：跨平台 UI，Windows 上使用云母 / 亚克力材质
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)：MVVM 源生成器
- Windows 原生 DWM 接口：标题栏、材质、窗口阴影

## 致谢

- [MediaMTX](https://github.com/bluenviron/mediamtx)：本项目仅为其外壳，全部转流能力由引擎提供
- [Avalonia](https://github.com/AvaloniaUI/Avalonia) 与 [FluentAvalonia](https://github.com/amwx/FluentAvalonia)
- [Filee](https://github.com/KnifeLemon/Filee)：窗口生命周期与若干 UI 手法的参照

## 许可

[MIT](LICENSE)。允许修改、闭源与商用，保留版权声明即可。

MediaMTX 引擎不随本仓库源码分发，遵循其自身许可证；`_x64_With_MediaMTX` **发布包**内含一份，同样按 MIT 授权。
