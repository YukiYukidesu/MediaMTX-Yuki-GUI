using System.Text.Json.Serialization;

namespace MediaMTX_Yuki_GUI.Services;

/// <summary>
/// Control API 里的一条路径，对应 <c>GET /v3/paths/list</c> 的 items 元素。
/// 只挑本程序要显示的那几个字段，其余一律忽略——接口字段很多，全映射过来只会增加维护面。
/// </summary>
public sealed class PathInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>有没有人往这条路径送流；引擎主动去拉时也算在线。</summary>
    [JsonPropertyName("online")]
    public bool Online { get; set; }

    /// <summary>流是否可读。在线但不可用，通常意味着还没收到关键帧。</summary>
    [JsonPropertyName("available")]
    public bool Available { get; set; }

    /// <summary>流是谁送进来的：推流连接的类型，或引擎自己去拉的源。</summary>
    [JsonPropertyName("source")]
    public PathSourceInfo? Source { get; set; }

    /// <summary>读端。本程序不做播放，正常情况下是空的。</summary>
    [JsonPropertyName("readers")]
    public List<PathReaderInfo>? Readers { get; set; }

    /// <summary>轨道列表，编解码格式在这里。<c>tracks</c> 那份是废弃字段，不要用。</summary>
    [JsonPropertyName("tracks2")]
    public List<PathTrackInfo>? Tracks { get; set; }

    /// <summary>累计收到的字节数，用来差分算入流码率。</summary>
    [JsonPropertyName("inboundBytes")]
    public long InboundBytes { get; set; }
}


/// <summary>路径的来源。</summary>
public sealed class PathSourceInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>来源类型，例如 rtmpConn、srtConn、rtspSource。</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
}


/// <summary>路径的一个读端。</summary>
public sealed class PathReaderInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
}


/// <summary>路径上的一条轨道。</summary>
public sealed class PathTrackInfo
{
    /// <summary>编解码格式，例如 H264、AAC。</summary>
    [JsonPropertyName("codec")]
    public string Codec { get; set; } = string.Empty;
}


/// <summary>
/// 某条路径下的一条转发，对应 <c>GET /v3/paths/forward-dests/list</c> 的 items 元素。
/// 与 metrics 的区别是这里带 <see cref="LastError"/>——失败原因只存在 API 里。
/// </summary>
public sealed class ForwardDestInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>在所属路径 forward 数组里的序号，从 1 开始，与 metrics 的 pos 标签同源。</summary>
    [JsonPropertyName("pos")]
    public int Position { get; set; }

    /// <summary>引擎内部状态：idle、forwarding、error。</summary>
    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    /// <summary>上一次失败的原因原文，没失败时是空串。</summary>
    [JsonPropertyName("lastError")]
    public string LastError { get; set; } = string.Empty;

    [JsonPropertyName("outboundBytes")]
    public long OutboundBytes { get; set; }
}


/// <summary><c>GET /v3/paths/list</c> 的应答外壳。</summary>
public sealed class PathListResponse
{
    [JsonPropertyName("itemCount")]
    public long ItemCount { get; set; }

    [JsonPropertyName("items")]
    public List<PathInfo>? Items { get; set; }
}


/// <summary><c>GET /v3/paths/forward-dests/list</c> 的应答外壳。</summary>
public sealed class ForwardDestListResponse
{
    [JsonPropertyName("itemCount")]
    public long ItemCount { get; set; }

    [JsonPropertyName("items")]
    public List<ForwardDestInfo>? Items { get; set; }
}


/// <summary>
/// 一段录像，对应 <c>GET /v3/recordings/list</c> 里某个 path 的 segments 元素。
/// 引擎是扫录制目录得出的，所以关掉录制之后历史片段照样看得到。
/// </summary>
public sealed class RecordingSegmentInfo
{
    /// <summary>
    /// 这一段的开始时间，RFC3339 原文。
    /// 留着原文是因为删除接口就按它匹配，自己转一遍格式再回传可能对不上。
    /// </summary>
    [JsonPropertyName("start")]
    public string Start { get; set; } = string.Empty;

    /// <summary>时长（秒）。fMP4 才有，MPEG-TS 或还在写的段给 null。</summary>
    [JsonPropertyName("duration")]
    public double? Duration { get; set; }
}


/// <summary>某个 path 下的全部录像。</summary>
public sealed class RecordingInfo
{
    /// <summary>path 名，也就是输入源里的「路径名」。</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("segments")]
    public List<RecordingSegmentInfo>? Segments { get; set; }
}


/// <summary><c>GET /v3/recordings/list</c> 的应答外壳。</summary>
public sealed class RecordingListResponse
{
    [JsonPropertyName("itemCount")]
    public long ItemCount { get; set; }

    [JsonPropertyName("items")]
    public List<RecordingInfo>? Items { get; set; }
}
