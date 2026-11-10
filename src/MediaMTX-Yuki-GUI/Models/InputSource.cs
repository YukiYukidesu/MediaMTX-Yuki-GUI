using System.Text.Json.Serialization;

namespace MediaMTX_Yuki_GUI.Models;

/// <summary>
/// 一路输入源。每一路在 MediaMTX 里对应一条 path，推流地址的最后一段就是 <see cref="Path"/>。
/// </summary>
public sealed class InputSource
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>备注名，纯给人看。</summary>
    public string Name { get; set; } = "";

    /// <summary>MediaMTX 的路径名，必须全小写字母数字下划线，且在输入源之间唯一。</summary>
    public string Path { get; set; } = "live";

    /// <summary>展示用的入口协议：rtmp / srt / rtsp，只影响推流地址的写法。</summary>
    public string Protocol { get; set; } = "rtmp";

    /// <summary>
    /// 该协议监听的端口；0 表示用协议默认端口。
    /// MediaMTX 一个协议只能监听一个端口，所以同一协议的多条输入共用这里的值。
    /// </summary>
    public int Port { get; set; }

    public bool Enabled { get; set; }

    /// <summary>
    /// 拉流地址：填了就让引擎主动去连这个地址，这条路径不再接收推流；
    /// 留空则由推流软件接入。形如 rtmp://host/app/stream 或 rtsp://…
    /// </summary>
    public string SourceUrl { get; set; } = "";


    /// <summary>
    /// 是否为这条输入开启本地录制。
    /// 录制是 MediaMTX 的 per-path 能力，写在这条 path 下就只录这一路；
    /// 简洁模式不写这个键（也不出界面），但本项照旧留着，切回高级模式即恢复。
    /// </summary>
    public bool Record { get; set; }

    /// <summary>
    /// 允许推这一路的账号 Id 列表（高级模式）。空列表表示不限制——任何已启用的账号都能推，
    /// 与「鉴权关着」时的全放行等价，这样老配置迁移过来不会突然推不上去。
    /// </summary>
    public List<string> PublishAccountIds { get; set; } = new();


    /// <summary>实际生效的端口：填了用填的，没填用协议默认。</summary>
    [JsonIgnore]
    public int EffectivePort
        => Port > 0 ? Port : InputProtocols.DefaultPort(Protocol);

    /// <summary>列表里显示的标题，没填备注就用路径名。</summary>
    [JsonIgnore]
    public string DisplayName
        => string.IsNullOrWhiteSpace(Name) ? Path : Name;
}
