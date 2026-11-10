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
    /// <summary>一条 <c>forward_dests_outbound_bytes{...} 12345</c> 记录。</summary>
    private static readonly Regex LinePattern = new(
        "^forward_dests_outbound_bytes\\{(?<labels>[^}]*)\\}\\s+(?<value>[0-9]+)$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>一个 <c>key="value"</c> 形式的标签。</summary>
    private static readonly Regex LabelPattern = new(
        "(?<key>[a-zA-Z_]+)=\"(?<value>[^\"]*)\"",
        RegexOptions.Compiled);

    /// <summary>解析出所有转发目标的指标，先按路径名、再按序号升序排列。</summary>
    public static IReadOnlyList<ForwardDestMetrics> ParseForwardDests(string? text)
    {
        List<ForwardDestMetrics> result = [];

        if (string.IsNullOrEmpty(text)) return result;

        foreach (Match match in LinePattern.Matches(text))
        {
            Dictionary<string, string> labels = ParseLabels(match.Groups["labels"].Value);

            string posText = labels.GetValueOrDefault("pos", string.Empty);
            string valueText = match.Groups["value"].Value;

            if (!int.TryParse(posText, out int position)) continue;
            if (!long.TryParse(valueText, out long bytes)) continue;

            result.Add(new ForwardDestMetrics(
                position,
                labels.GetValueOrDefault("path", string.Empty),
                labels.GetValueOrDefault("state", string.Empty),
                bytes));
        }

        result.Sort((left, right) =>
        {
            int byPath = string.CompareOrdinal(left.Path, right.Path);

            return byPath != 0 ? byPath : left.Position.CompareTo(right.Position);
        });

        return result;
    }

    /// <summary>把标签串拆成字典。</summary>
    private static Dictionary<string, string> ParseLabels(string raw)
    {
        Dictionary<string, string> labels = [];

        foreach (Match match in LabelPattern.Matches(raw))
        {
            labels[match.Groups["key"].Value] = match.Groups["value"].Value;
        }

        return labels;
    }
}
