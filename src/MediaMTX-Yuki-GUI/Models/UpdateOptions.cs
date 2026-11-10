using MediaMTX_Yuki_GUI.Services;

namespace MediaMTX_Yuki_GUI.Models;

/// <summary>更新检查相关配置。仓库名留空就不检查对应的那一路。</summary>
public sealed class UpdateOptions
{
    /// <summary>本程序的 GitHub 仓库（owner/repo）。本地开发阶段没有发布源，默认留空。</summary>
    public string AppRepo { get; set; } = string.Empty;

    /// <summary>引擎的 GitHub 仓库，MediaMTX 发行版就在这。</summary>
    public string EngineRepo { get; set; } = "bluenviron/mediamtx";

    /// <summary>启动时检查本程序有没有新版本。程序还没建库时拿不到，静默跳过。</summary>
    public bool CheckAppOnStartup { get; set; }

    /// <summary>启动时检查 MediaMTX 引擎有没有新版本。</summary>
    public bool CheckEngineOnStartup { get; set; } = true;

    /// <summary>本程序的更新源 Id，见 SourceCatalog。</summary>
    public string AppSource { get; set; } = SourceCatalog.GitHubId;

    /// <summary>引擎的更新源 Id，见 SourceCatalog。</summary>
    public string EngineSource { get; set; } = SourceCatalog.GitHubId;

    /// <summary>查更新与下载安装包时走哪条代理。</summary>
    public ProxyOptions Proxy { get; set; } = new();

    /// <summary>
    /// 老字段，0.5.0 之前只有一个总开关。保留是为了让老配置能读进来，
    /// 迁移段会把它拆成上面两个开关再置为 false。
    /// </summary>
    public bool CheckOnStartup { get; set; }
}
