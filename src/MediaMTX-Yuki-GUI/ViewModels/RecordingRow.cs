using System.Globalization;

namespace MediaMTX_Yuki_GUI.ViewModels;

/// <summary>
/// 录像窗口里的一行，对应引擎返回的一段录像。
/// 只负责把时间与时长排成人能看的样子，删除时用的还是 <see cref="Start"/> 原文。
/// </summary>
public sealed class RecordingRow
{
    /// <summary>开始时间，接口返回的 RFC3339 原文，删除时原样回传。</summary>
    public string Start { get; init; } = string.Empty;

    /// <summary>开始时间，按本地时区排过序的样子，解析不了就退回原文。</summary>
    public string StartText { get; init; } = string.Empty;

    /// <summary>时长文案。引擎还没写完这一段时不带时长，显示一个破折号。</summary>
    public string DurationText { get; init; } = string.Empty;

    /// <summary>把引擎给的一段时间排成一行。</summary>
    /// <param name="start">开始时间的 RFC3339 原文。</param>
    /// <param name="durationSeconds">秒数，null 表示引擎没给（MPEG-TS 或还在写）。</param>
    public static RecordingRow From(string start, double? durationSeconds)
    {
        string startText = DateTimeOffset.TryParse(
            start, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset parsed)
            ? parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            : start;

        string durationText = durationSeconds is null
            ? "—"
            : TimeSpan.FromSeconds(durationSeconds.Value).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);

        return new RecordingRow
        {
            Start = start,
            StartText = startText,
            DurationText = durationText,
        };
    }
}
