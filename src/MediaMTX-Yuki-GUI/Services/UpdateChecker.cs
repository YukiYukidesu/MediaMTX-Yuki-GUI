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
        CancellationToken ct = default,
        bool includePrerelease = false)
    {
        if (string.IsNullOrWhiteSpace(repo)) return null;

        // 两种查法结果可能不同，缓存必须分开存，否则先查正式版的那一路会把
        // 后查预发布的那一路覆盖掉（或者反过来）
        string cacheKey = includePrerelease ? repo + "#pre" : repo;

        if (!force && TryReadCache(cacheKey, out string cached)) return cached;

        try
        {
            SourceCatalog.UpdateSource source = SourceCatalog.Find(sourceId);

            string? value = includePrerelease
                ? await ReadLatestFromListAsync(source, repo, ct).ConfigureAwait(false)
                : await ReadLatestStableAsync(source, repo, ct).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(value)) return null;

            WriteCache(cacheKey, value);

            return value;
        }
        catch (Exception)
        {
            // 离线、超时、被墙都走这里，静默跳过
            return null;
        }
    }

    /// <summary>走 <c>/releases/latest</c> 只拿正式版。</summary>
    private async Task<string?> ReadLatestStableAsync(
        SourceCatalog.UpdateSource source,
        string repo,
        CancellationToken ct)
    {
        using HttpResponseMessage response = await _net.Http
            .GetAsync(SourceCatalog.LatestReleaseUrl(source, repo), ct)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        using JsonDocument document = JsonDocument.Parse(body);

        if (!document.RootElement.TryGetProperty("tag_name", out JsonElement tag)) return null;

        return tag.GetString();
    }

    /// <summary>
    /// 走 <c>/releases</c> 列表，把预发布一起收进来，按版本号挑最大的那个。
    /// 列表是「按创建时间倒序」而不是「按版本号」，所以不能直接取第一条——
    /// 补发一个老版本的修复包时，第一条比真·最新版还老。
    /// </summary>
    private async Task<string?> ReadLatestFromListAsync(
        SourceCatalog.UpdateSource source,
        string repo,
        CancellationToken ct)
    {
        using HttpResponseMessage response = await _net.Http
            .GetAsync(SourceCatalog.ReleasesUrl(source, repo), ct)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode) return null;

        string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        using JsonDocument document = JsonDocument.Parse(body);

        if (document.RootElement.ValueKind != JsonValueKind.Array) return null;

        string? best = null;
        SemanticVersion bestVersion = default;

        foreach (JsonElement release in document.RootElement.EnumerateArray())
        {
            // 草稿在这里根本不会返回，但标记了 draft 的仍可能来自别的 key，保险起见跳过
            if (release.TryGetProperty("draft", out JsonElement draft) && draft.ValueKind == JsonValueKind.True)
            {
                continue;
            }

            if (!release.TryGetProperty("tag_name", out JsonElement tag)) continue;

            string? candidate = tag.GetString();

            if (string.IsNullOrWhiteSpace(candidate)) continue;

            if (!SemanticVersion.TryParse(candidate, out SemanticVersion version)) continue;

            if (best is null || version.CompareTo(bestVersion) > 0)
            {
                best = candidate;
                bestVersion = version;
            }
        }

        return best;
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
