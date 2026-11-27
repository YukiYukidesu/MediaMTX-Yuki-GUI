using System.Text;

namespace MediaMTX_Yuki_GUI.Services;

/// <summary>单条转发目标在引擎侧的实时指标。</summary>
/// <param name="Position">转发在所属路径里的序号，从 1 开始，与该路径 forward 数组的下标 + 1 一致。</param>
/// <param name="Path">所属路径名。多条输入源各是一条路径，序号只在同一路径内可比。</param>
/// <param name="State">引擎内部状态，例如 idle、forwarding、error。</param>
/// <param name="OutboundBytes">累计发出的字节数，用于差分算码率。</param>
public sealed record ForwardDestMetrics(int Position, string Path, string State, long OutboundBytes);

/// <summary>把 Prometheus 文本格式的 metrics 解析成结构化数据。</summary>
public static class MetricsParser
{
    private const string MetricPrefix = "forward_dests_outbound_bytes{";

    /// <summary>边读边解析，只留转发目标那几行，其余直接丢弃；先按路径名、再按序号升序排列。</summary>
    /// <remarks>
    /// 响应体大小跟着路径数和连接数走：本地 11 条路径、25 个转发目标时，?type=forward_dests 只有 6.5 KB，
    /// 全量 /metrics 也才 13.8 KB；但路径上百、SRT 连接一多就是另一个量级（每条 SRT 连接光指标就几十行）。
    /// 所以不能先读成字符串再找，那样一个大字符串直接进大对象堆，一次回收就把界面停住近一秒。
    /// 这里按行流式处理，分配量只跟需要的数据条数成正比。
    /// 流由调用方负责释放，本方法只读不关。
    /// </remarks>
    public static async Task<List<ForwardDestMetrics>> ParseForwardDestsAsync(Stream stream, CancellationToken ct)
    {
        List<ForwardDestMetrics> result = [];

        using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 8192, leaveOpen: true);

        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            ForwardDestMetrics? metric = TryParseLine(line.AsSpan());

            if (metric is not null) result.Add(metric);
        }

        result.Sort(CompareByPathAndPosition);

        return result;
    }

    /// <summary>解析一行 <c>forward_dests_outbound_bytes{...} 123</c>，前缀不匹配或字段缺失返回 null。</summary>
    private static ForwardDestMetrics? TryParseLine(ReadOnlySpan<char> line)
    {
        if (!line.StartsWith(MetricPrefix, StringComparison.Ordinal)) return null;

        int close = line.IndexOf('}');

        if (close < 0) return null;

        ReadOnlySpan<char> labels = line[..close];
        ReadOnlySpan<char> tail = line[(close + 1)..].Trim();

        if (!int.TryParse(ReadLabel(labels, "pos"), out int position)) return null;
        if (!long.TryParse(tail, out long bytes)) return null;

        return new ForwardDestMetrics(
            position,
            ReadLabel(labels, "path").ToString(),
            ReadLabel(labels, "state").ToString(),
            bytes);
    }

    /// <summary>先按路径名、再按序号升序。</summary>
    private static int CompareByPathAndPosition(ForwardDestMetrics left, ForwardDestMetrics right)
    {
        int byPath = string.CompareOrdinal(left.Path, right.Path);

        return byPath != 0 ? byPath : left.Position.CompareTo(right.Position);
    }

    /// <summary>从标签串里取一个值，取不到给空串。</summary>
    private static ReadOnlySpan<char> ReadLabel(ReadOnlySpan<char> labels, string key)
    {
        string needle = key + "=\"";

        int at = labels.IndexOf(needle.AsSpan(), StringComparison.Ordinal);

        if (at < 0) return default;

        ReadOnlySpan<char> tail = labels[(at + needle.Length)..];
        int close = tail.IndexOf('"');

        return close < 0 ? default : tail[..close];
    }
}
