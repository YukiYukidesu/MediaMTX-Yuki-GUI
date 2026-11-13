using MediaMTX_Yuki_GUI.Services;

namespace MediaMTX_Yuki_GUI.Models;

/// <summary>更新检查相关配置。仓库名留空就不检查对应的那一路。</summary>
public sealed class UpdateOptions
{
    /// <summary>官方仓库。程序自己发布的版本在这儿，不用让用户填。</summary>
    public const string DefaultAppRepo = "YukiYukidesu/MediaMTX-Yuki-GUI";

    /// <summary>
    /// 本程序的 GitHub 仓库（owner/repo）。
    /// <para>
    /// **不要用空串表示「不检查」**：老配置里那个字段早就落成了空串，光改属性初始化器救不回来
    /// （System.Text.Json 见到空的 appRepo 会照样反序列化成空串，初始化器不参与）。
    /// 因此取用时一律走 <see cref="AppRepoOrDefault"/>，空串按官方仓库处理；
    /// 想不查就别开启动检查、也别点那颗按钮。
    /// </para>
    /// </summary>
    public string AppRepo { get; set; } = DefaultAppRepo;

    /// <summary>取真正要查的仓库：为空（老配置遗留）就回落到官方仓库。</summary>
    public string AppRepoOrDefault
        => string.IsNullOrWhiteSpace(AppRepo) ? DefaultAppRepo : AppRepo.Trim();

    /// <summary>引擎的 GitHub 仓库，MediaMTX 发行版就在这。</summary>
    public string EngineRepo { get; set; } = "bluenviron/mediamtx";

    /// <summary>本程序更新只看正式版。</summary>
    public const string AppChannelStable = "stable";

    /// <summary>本程序更新把预发布版也算上。</summary>
    public const string AppChannelPrerelease = "prerelease";

    /// <summary>引擎更新按 GitHub 上的最新正式版走。</summary>
    public const string EngineChannelLatest = "latest";

    /// <summary>引擎更新锁到 <see cref="ProgramVersion.RecommendedEngineVersion"/>，版本不符就换回去。</summary>
    public const string EngineChannelRecommended = "recommended";

    /// <summary>
    /// 本程序更新查哪个通道。
    /// <para>
    /// 默认预发布：本程序自己就是 alpha，只查正式版会永远查不到东西
    /// （GitHub 的 /releases/latest 会跳过预发布）。
    /// </para>
    /// </summary>
    public string AppChannel { get; set; } = AppChannelPrerelease;

    /// <summary>
    /// 引擎更新查哪个通道。默认最新版本；切到推荐适配版本之后不再看远端，
    /// 只跟程序里写死的推荐版本比。
    /// </summary>
    public string EngineChannel { get; set; } = EngineChannelLatest;

    /// <summary>启动时检查本程序有没有新版本。查不到时静默跳过，不打扰用户。</summary>
    public bool CheckAppOnStartup { get; set; } = true;

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
