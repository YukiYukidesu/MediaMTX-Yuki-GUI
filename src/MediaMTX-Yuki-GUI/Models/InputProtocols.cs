namespace MediaMTX_Yuki_GUI.Models;

/// <summary>
/// 推流入口协议。每种协议在 MediaMTX 里只能监听一个端口，
/// 所以同一协议的多条输入源共用同一个端口，区别只在路径名。
/// </summary>
public static class InputProtocols
{
    public const string Rtmp = "rtmp";
    public const string Rtsp = "rtsp";
    public const string Srt = "srt";

    /// <summary>协议默认端口；用户没填端口时就是它。</summary>
    public static int DefaultPort(string? protocol)
        => (protocol ?? Rtmp).ToLowerInvariant() switch
        {
            Rtsp => 8554,
            Srt => 8890,
            _ => 1935,
        };

    /// <summary>界面上显示的名字。</summary>
    public static string DisplayName(string? protocol)
        => (protocol ?? Rtmp).ToLowerInvariant() switch
        {
            Rtsp => "RTSP",
            Srt => "SRT",
            _ => "RTMP",
        };

    /// <summary>按协议拼出给 OBS 填的推流地址。</summary>
    public static string BuildPushUrl(string? protocol, int port, string? path)
    {
        string name = string.IsNullOrWhiteSpace(path) ? "live" : path;
        int actual = port > 0 ? port : DefaultPort(protocol);

        return (protocol ?? Rtmp).ToLowerInvariant() switch
        {
            Rtsp => "rtsp://127.0.0.1:" + actual + "/" + name,
            Srt => "srt://127.0.0.1:" + actual + "?streamid=publish:" + name,
            _ => "rtmp://127.0.0.1:" + actual + "/" + name,
        };
    }

    /// <summary>除了路径名，OBS 里还要填什么才推得上来。</summary>
    public static string HintFor(string? protocol)
        => (protocol ?? Rtmp).ToLowerInvariant() switch
        {
            Rtsp => Services.Localization.Instance["l008-008"],
            Srt => Services.Localization.Instance["l008-009"],
            _ => Services.Localization.Instance["l008-010"],
        };
}
