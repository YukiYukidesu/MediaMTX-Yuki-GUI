using System.Text.Json.Serialization;
using MediaMTX_Yuki_GUI.Services;

namespace MediaMTX_Yuki_GUI.Models;

/// <summary>一个转发目标。服务器与推流码分开存，拼装规则交给 DestBuilder。</summary>
public sealed class ForwardTarget
{
    public string Id        { get; set; } = Guid.NewGuid().ToString("N");
    public string Name      { get; set; } = string.Empty;
    public string Platform  { get; set; } = "custom";

    /// <summary>服务器地址，例如 rtmp://live-push.bilivideo.com/live-bvc/。</summary>
    public string Server    { get; set; } = string.Empty;

    /// <summary>推流码，例如 ?streamname=xxx&amp;key=yyy。</summary>
    public string StreamKey { get; set; } = string.Empty;

    /// <summary>绑定的输入源 Id。空字符串表示还没指定，这条目标不会出现在任何 path 下。</summary>
    public string InputSourceId { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 这条目标走哪条代理：follow 跟随设置页的默认推流代理 / direct 直连 / custom 用 ProxyUrl。
    /// 平台被墙（YouTube、Twitch）时才需要，国内平台一律直连最快。
    /// </summary>
    public string ProxyMode { get; set; } = ProxyOptions.Follow;

    /// <summary>ProxyMode 为 custom 时使用的代理地址，形如 socks5://127.0.0.1:8888。</summary>
    public string ProxyUrl { get; set; } = string.Empty;

    /// <summary>拼成 MediaMTX forward 的 dest 字段。</summary>
    public string BuildDest() => DestBuilder.Compose(Server, StreamKey);

    /// <summary>给界面显示的名字，空名字时退化成平台名或「自定义」。只用于显示，不写进配置。</summary>
    [JsonIgnore]
    public string DisplayName
        => string.IsNullOrWhiteSpace(Name)
            ? PresetCatalog.Find(Platform).DisplayName
            : Name.Trim();

    /// <summary>是不是自定义代理档，界面上只有这一档才允许编辑地址。</summary>
    [JsonIgnore]
    public bool HasCustomProxy => ProxyMode == "custom";
}
