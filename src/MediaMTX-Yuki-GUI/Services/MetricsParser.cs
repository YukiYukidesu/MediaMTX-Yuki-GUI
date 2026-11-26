using System.Text.RegularExpressions;

namespace MediaMTX_Yuki_GUI.Services;

/// <summary>单条转发目标在引擎侧的实时指标。</summary>
/// <param name="Position">转发在所属路径里的序号，从 1 开始，与该路径 forward 数组的下标 + 1 一致。</param>
/// <param name="Path">所属路径名。多条输入源各是一条路径，序号只在同一路径内可比。</param>
/// <param name="State">引擎内部状态，例如 idle、forwarding、error。</param>
/// <param name="OutboundBytes">累计发出的字节数，用于差分算码率。</param>
public sealed record ForwardDestMetrics(int Position, string Path, string State, long OutboundBytes);

/// <summary>把 Prometheus 文本格式的 metrics 文本解析成结构化数据。</summary>
public static class MetricsParser
{
    private const string MetricPrefix = "forward_dests_outbound_bytes{";

    /// <summary>一个 <c>key="value"</c> 形式的标签。</summary>
    private static readonly Regex LabelPattern = new(
        "(?<key>[a-zA-Z_]+)=\"(?<value>[^\"]*)\"",
        RegexOptions.Compiled);

    /// <summary>解析出所有转发目标的指标，先按路径名、再按序号升序排列。</summary>
    public static IReadOnlyList<ForwardDestMetrics> ParseForwardDests(string? text)
    {
        List<ForwardDestMetrics> result = [];

        if (string.IsNullOrEmpty(text)) return result;

        // 整份 metrics 有几千万字节：按 Span 逐段扫描，不 Split、不为每行建字符串，
        // 否则光是拆分就会产生上百万个小对象，一轮一次 Gen2 回收就能把界面停住近一秒
        ReadOnlySpan<char> span = text.AsSpan();
        int cursor = 0;

        while (cursor < span.Length)
        {
            int start = span[cursor..].IndexOf(MetricPrefix, StringComparison.Ordinal);

            if (start < 0) break;

            start += cursor;

            int end = span[start..].IndexOf('\n');

            if (end < 0) end = span.Length - start;

            ReadOnlySpan<char> line = span.Slice(start, end);
            cursor = start + end + 1;

            int close = line.IndexOf('}');

            if (close < 0) continue;

            ReadOnlySpan<char> labels = line[..close];
            ReadOnlySpan<char> tail = line[(close + 1)..].Trim();

            if (!int.TryParse(ReadLabel(labels, "pos"), out int position)) continue;
            if (!long.TryParse(tail, out long bytes)) continue;

            result.Add(new ForwardDestMetrics(
                position,
                ReadLabel(labels, "path").ToString(),
                ReadLabel(labels, "state").ToString(),
                bytes));
        }

        result.Sort((left, right) =>
        {
            int byPath = string.CompareOrdinal(left.Path, right.Path);

            return byPath != 0 ? byPath : left.Position.CompareTo(right.Position);
        });

        return result;
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