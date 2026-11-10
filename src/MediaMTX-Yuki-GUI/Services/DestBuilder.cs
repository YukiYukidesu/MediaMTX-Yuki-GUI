namespace MediaMTX_Yuki_GUI.Services;

/// <summary>
/// 把「服务器地址 + 推流码」拼成 MediaMTX 能正确解析的 forward dest。
/// 实测结论：MediaMTX 只把 # 后面那一段当作推流码，?query 会被整条塞进 app。
/// </summary>
public static class DestBuilder
{
    // ====== 拼装 ======

    /// <summary>拼成 server#key；推流码为空时原样返回服务器地址。</summary>
    public static string Compose(string? server, string? streamKey)
    {
        string cleanServer = (server ?? string.Empty).Trim();
        string cleanKey = (streamKey ?? string.Empty).Trim();

        if (cleanServer.Length == 0) return string.Empty;
        if (cleanKey.Length == 0) return cleanServer;

        // 用户自己写过 # 了，别再叠一个
        if (cleanServer.IndexOf('#') >= 0) return cleanServer;

        return cleanServer + "#" + cleanKey;
    }

    // ====== 拆分 ======

    /// <summary>
    /// 把用户粘的一整条推流地址拆成服务器与推流码。
    /// 拆不出来时返回 false，调用方应把整条当成服务器地址。
    /// </summary>
    public static bool TrySplitFullUrl(string? raw, out string server, out string streamKey)
    {
        server = string.Empty;
        streamKey = string.Empty;

        if (string.IsNullOrWhiteSpace(raw)) return false;

        string text = raw.Trim();

        // 已经带 #，说明是手工拆过的完整 dest，不再二次拆分
        if (text.IndexOf('#') >= 0) return false;

        int query = text.IndexOf('?');
        if (query >= 0) return TrySplitAtQuery(text, query, out server, out streamKey);

        return TrySplitAtLastSegment(text, out server, out streamKey);
    }

    /// <summary>query 型：B站、斗鱼这类，推流码是整条 ?...&amp;... 。</summary>
    private static bool TrySplitAtQuery(string text, int query, out string server, out string streamKey)
    {
        server = string.Empty;
        streamKey = string.Empty;

        int slash = text.LastIndexOf('/', query - 1);
        if (slash < 0) return false;

        server = text[..(slash + 1)];
        streamKey = text[(slash + 1)..];

        return streamKey.Length > 0;
    }

    /// <summary>路径型：Twitch、YouTube、抖音这类，推流码是最后一段。</summary>
    private static bool TrySplitAtLastSegment(string text, out string server, out string streamKey)
    {
        server = string.Empty;
        streamKey = string.Empty;

        int schemeEnd = text.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0) return false;

        int pathStart = text.IndexOf('/', schemeEnd + 3);
        if (pathStart < 0) return false;

        // path 至少两段才可能是「服务器 / 推流码」，只有一段说明这就是纯服务器地址
        string path = text[pathStart..];
        if (path.Count(c => c == '/') < 2) return false;

        int lastSlash = text.LastIndexOf('/');
        if (lastSlash <= pathStart) return false;

        server = text[..(lastSlash + 1)];
        streamKey = text[(lastSlash + 1)..];

        return streamKey.Length > 0;
    }
}
