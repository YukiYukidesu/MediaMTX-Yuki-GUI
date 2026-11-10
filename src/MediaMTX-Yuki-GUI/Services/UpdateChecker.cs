using System.IO;
using System.Net.Http;
using System.Text.Json;
using MediaMTX_Yuki_GUI.Models;

namespace MediaMTX_Yuki_GUI.Services;

/// <summary>
/// 查 GitHub 最新 release 的 tag，结果在本地缓存 24 小时。
/// 网络不通、被墙、限流一律当作「不知道有没有新版本」，不打扰用户，也绝不阻塞启动。
/// 出站走 <see cref="NetClient"/>，也就是跟随设置页里选的那条代理。
/// </summary>
public sealed class UpdateChecker
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(24);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly NetClient _net;

    /// <summary>走哪条代理去查，由外部注入（设置页里那句「更新下载代理」）。</summary>
    /// <param name="proxy">取当前代理设置的回调；不给就直连。</param>
    public UpdateChecker(Func<ProxyOptions?>? proxy = null)
    {
        _net = new NetClient(proxy ?? (() => null));
    }

    private string CachePath => Path.Combine(AppPaths.ConfigDir, "update-cache.json");

    /// <summary>拿仓库最新的 tag（形如 v1.21.3）；拿不到返回 null。</summary>
    /// <param name="repo">owner/repo，留空表示这一路不检查。</param>
    /// <param name="sourceId">更新源 Id，见 <see cref="SourceCatalog"/>；不认识的一律回落官方。</param>
    /// <param name="force">true 表示跳过本地缓存，用户亲手点了「立即检查」时才用。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<string?> GetLatestTagAsync(
        string repo,
        string? sourceId = null,
        bool force = false,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(repo)) return null;

        if (!force && TryReadCache(repo, out string cached)) return cached;

        try
        {
            SourceCatalog.UpdateSource source = SourceCatalog.Find(sourceId);

            using HttpResponseMessage response = await _net.Http
                .GetAsync(SourceCatalog.LatestReleaseUrl(source, repo), ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode) return null;

            string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            using JsonDocument document = JsonDocument.Parse(body);

            if (!document.RootElement.TryGetProperty("tag_name", out JsonElement tag)) return null;

            string? value = tag.GetString();
            if (string.IsNullOrWhiteSpace(value)) return null;

            WriteCache(repo, value);

            return value;
        }
        catch (Exception)
        {
            // 离线、超时、被墙都走这里，静默跳过
            return null;
        }
    }

    // ====== 缓存 ======

    private sealed class CacheEntry
    {
        public string Tag { get; set; } = string.Empty;
        public DateTimeOffset CheckedAt { get; set; }
    }

    private Dictionary<string, CacheEntry> LoadCache()
    {
        try
        {
            if (!File.Exists(CachePath)) return [];

            string json = File.ReadAllText(CachePath);

            return JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(json, JsonOptions) ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    private bool TryReadCache(string repo, out string tag)
    {
        tag = string.Empty;

        Dictionary<string, CacheEntry> cache = LoadCache();

        if (!cache.TryGetValue(repo, out CacheEntry? entry) || entry is null) return false;
        if (DateTimeOffset.Now - entry.CheckedAt > CacheLifetime) return false;
        if (string.IsNullOrWhiteSpace(entry.Tag)) return false;

        tag = entry.Tag;
        return true;
    }

    private void WriteCache(string repo, string tag)
    {
        try
        {
            Dictionary<string, CacheEntry> cache = LoadCache();

            cache[repo] = new CacheEntry
            {
                Tag = tag,
                CheckedAt = DateTimeOffset.Now,
            };

            string json = JsonSerializer.Serialize(cache, JsonOptions);
            File.WriteAllText(CachePath, json);
        }
        catch (Exception)
        {
            // 缓存写不进去也无所谓，下次重新联网查一遍
        }
    }
}
