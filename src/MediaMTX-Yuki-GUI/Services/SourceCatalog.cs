namespace MediaMTX_Yuki_GUI.Services;

/// <summary>
/// 查更新与下载安装包的源。查版本和下载是两条不同的 URL：
/// 官方 API 在 api.github.com，加速站只代理 github.com 上的下载，
/// 所以每个源同时给出 ApiBase 与 DownloadBase，由调用方各取所需。
/// </summary>
public static class SourceCatalog
{
    /// <summary>官方源，永远是默认值。</summary>
    public const string GitHubId = "github";

    /// <summary>一个可选的更新源。<paramref name="NameKey"/> 是语言包的键，显示时才翻译。</summary>
    public sealed record UpdateSource(
        string Id,
        string NameKey,
        string ApiBase,
        string DownloadBase);

    /// <summary>
    /// 全部可选源。加速站只改 DownloadBase，ApiBase 一律走官方——
    /// 公共加速站大多不代理 API，连着试只会白等超时。
    /// </summary>
    public static readonly IReadOnlyList<UpdateSource> All =
    [
        new(GitHubId,           "c016-002", "https://api.github.com", "https://github.com"),
        new("ghproxy",          "c016-003", "https://api.github.com", "https://ghproxy.net/https://github.com"),
        new("ghfast",           "c016-004", "https://api.github.com", "https://ghfast.top/https://github.com"),
        new("gh-proxy",         "c016-005", "https://api.github.com", "https://gh-proxy.com/https://github.com"),
        new("kkgithub",         "c016-006", "https://api.github.com", "https://github.kkgithub.com"),
    ];

    /// <summary>按 Id 找源，找不到一律回落官方。</summary>
    public static UpdateSource Find(string? id)
        => All.FirstOrDefault(source => source.Id == id) ?? All[0];

    /// <summary>拼「查最新 release」的 URL。</summary>
    public static string LatestReleaseUrl(UpdateSource source, string repo)
        => source.ApiBase + "/repos/" + repo + "/releases/latest";

    /// <summary>拼「下载某个 release 资产」的 URL。</summary>
    public static string AssetUrl(UpdateSource source, string repo, string tag, string assetName)
        => source.DownloadBase + "/" + repo + "/releases/download/" + tag + "/" + assetName;
}
