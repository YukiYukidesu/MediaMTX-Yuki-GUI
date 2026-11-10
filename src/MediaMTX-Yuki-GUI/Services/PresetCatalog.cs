namespace MediaMTX_Yuki_GUI.Services;


using L = MediaMTX_Yuki_GUI.Services.Localization;
/// <summary>一个内置平台模板。服务器地址固定的平台，用户只需再填推流码。</summary>
public sealed record PlatformPreset(
    string Id,
    string DisplayName,
    string ServerPrefix,
    string Hint)
{
    /// <summary>服务器地址是否固定。为 false 时提示用户整条粘贴。</summary>
    public bool HasFixedServer => ServerPrefix.Length > 0;
}

/// <summary>内置平台模板表。地址随时会被平台改，这里只是省打字，不作保证。</summary>
public static class PresetCatalog
{
    public const string CustomId = "custom";

    public static readonly IReadOnlyList<PlatformPreset> All = new List<PlatformPreset>
    {
        new("bilibili", L.T("c009-005"), "rtmp://live-push.bilivideo.com/live-bvc/",
            L.T("l009-002")),

        new("douyin", L.T("c009-006"), "rtmp://push-rtmp-l6-c.douyincdn.com/stage/",
            L.T("l009-003")),

        new("kuaishou", L.T("c009-007"), "",
            L.T("l009-004")),

        new("youtube", "YouTube", "rtmps://a.rtmps.youtube.com/live2",
            L.T("c009-008")),

        new("twitch", "Twitch", "rtmp://live.twitch.tv/app/",
            L.T("c009-009")),

        new("huya", L.T("c009-010"), "",
            L.T("l009-005")),

        new("douyu", L.T("c009-011"), "",
            L.T("l009-006")),

        new(CustomId, L.T("c009-012"), "",
            L.T("l009-007")),
    };

    /// <summary>按 id 找模板，找不到时回退到「自定义」。</summary>
    public static PlatformPreset Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return ById(CustomId);

        return All.FirstOrDefault(p => p.Id == id) ?? ById(CustomId);
    }

    private static PlatformPreset ById(string id)
        => All.First(p => p.Id == id);
}
