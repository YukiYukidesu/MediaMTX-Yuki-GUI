using System.Reflection;

namespace MediaMTX_Yuki_GUI.Services;

/// <summary>
/// 程序版本号的唯一来源，界面、日志、app.json 都从这里取。
/// 分散算两遍迟早会算出不一样的字符串，所以只留这一个出口。
///
/// **它同时是配置版本号的唯一依据**：配置里那个数字只用来跟这里比大小，
/// 不再另维护一份"配置结构版本"常量——两套号必然分叉，一套就够。
/// </summary>
public static class ProgramVersion
{
    /// <summary>
    /// 带 v 前缀的显示串，例如 v0.0.1。
    /// 数字本体来自 csproj 的 &lt;Version&gt;，改了那边这里自动跟着变。
    /// </summary>
    public static string Text { get; } =
        "v" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0");

    /// <summary>
    /// 版本号的整数编码，<b>配置升级链拿它当"当前版本"</b>。
    /// 编码规则：<c>major * 1_000_000 + minor * 1_000 + patch</c>。
    /// 例：0.0.1 → 1（打印成九位串是 000000001），0.1.0 → 1000（000001000），1.0.0 → 1000000（001000000）。
    /// </summary>
    public static int Code { get; } = Encode(Version);

    /// <summary>三段版本号，取不到时退回 0.0.0。</summary>
    private static Version Version
        => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    /// <summary>把三段版本号编码成整数，越大表示越新。</summary>
    public static int Encode(int major, int minor, int patch)
        => major * 1_000_000 + minor * 1_000 + patch;

    private static int Encode(Version v)
        => Encode(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));

    /// <summary>把编码后的数字还原成 "主.次.修"，用于日志与排错。</summary>
    public static string ToText(int code)
    {
        int major = code / 1_000_000;
        int minor = code / 1_000 % 1_000;
        int patch = code % 1_000;

        return major + "." + minor + "." + patch;
    }

    /// <summary>把编码后的数字打印成九位串（三段各补三位），用于日志与文档对照。</summary>
    public static string ToNineDigits(int code)
        => (code / 1_000_000).ToString("000")
           + (code / 1_000 % 1_000).ToString("000")
           + (code % 1_000).ToString("000");
}
