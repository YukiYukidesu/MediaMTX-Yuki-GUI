namespace MediaMTX_Yuki_GUI.Services;

/// <summary>
/// 读 Windows 的系统代理（WinINET 的 Internet Settings）。
/// 用 reg.exe 而不是 Microsoft.Win32.Registry：本项目 TFM 是 net10.0 而不是 net10.0-windows，
/// 引注册表 API 要额外加包，而 Reg 已经有一份 reg.exe 封装，没必要再引第二套。
/// </summary>
public static class SystemProxy
{
    private const string InternetSettings =
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    /// <summary>
    /// 读出可用的系统代理地址（形如 http://127.0.0.1:7897）。
    /// 系统没配代理、或者配置读不出来都返回 null，调用方据此回落直连。
    /// </summary>
    public static string? Read()
    {
        string? fromRegistry = ReadRegistry();
        if (!string.IsNullOrWhiteSpace(fromRegistry)) return fromRegistry;

        return ReadEnvironment();
    }

    // ====== 注册表 ======

    private static string? ReadRegistry()
    {
        (int code, string output) = Reg.Run("query \"" + InternetSettings + "\" /v ProxyEnable");
        if (code != 0) return null;

        // REG_DWORD 那一行形如「    ProxyEnable    REG_DWORD    0x1」
        if (!output.Contains("0x1", StringComparison.OrdinalIgnoreCase)) return null;

        (int serverCode, string serverOutput) = Reg.Run("query \"" + InternetSettings + "\" /v ProxyServer");
        if (serverCode != 0) return null;

        string raw = ExtractValue(serverOutput, "ProxyServer");
        if (string.IsNullOrWhiteSpace(raw)) return null;

        return Normalize(raw);
    }

    /// <summary>从 reg query 的输出里抠出某个值的数据部分。</summary>
    private static string ExtractValue(string output, string valueName)
    {
        foreach (string line in output.Split('\n'))
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith(valueName, StringComparison.OrdinalIgnoreCase)) continue;

            int marker = trimmed.IndexOf("REG_SZ", StringComparison.OrdinalIgnoreCase);
            if (marker < 0) continue;

            return trimmed[(marker + "REG_SZ".Length)..].Trim();
        }

        return string.Empty;
    }

    // ====== 环境变量 ======

    private static string? ReadEnvironment()
    {
        foreach (string name in new[] { "HTTPS_PROXY", "HTTP_PROXY", "ALL_PROXY" })
        {
            string? value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        }

        return null;
    }

    // ====== 归一化 ======

    /// <summary>
    /// ProxyServer 可能是这几种形态：
    ///   127.0.0.1:7897
    ///   http=127.0.0.1:8080;https=127.0.0.1:8080
    ///   socks=127.0.0.1:1080
    /// 统一折成一个带协议头的地址。
    /// </summary>
    internal static string Normalize(string raw)
    {
        string text = raw.Trim();

        if (text.Contains('='))
        {
            string? picked = PickFromList(text, "https") ?? PickFromList(text, "http") ?? PickFromList(text, "socks");
            if (picked is null) return text;

            bool isSocks = text.Contains("socks=", StringComparison.OrdinalIgnoreCase);
            return (isSocks ? "socks5://" : "http://") + picked;
        }

        return text.Contains("://") ? text : "http://" + text;
    }

    private static string? PickFromList(string raw, string scheme)
    {
        foreach (string part in raw.Split(';'))
        {
            int split = part.IndexOf('=');
            if (split <= 0) continue;

            if (!part[..split].Trim().Equals(scheme, StringComparison.OrdinalIgnoreCase)) continue;

            string value = part[(split + 1)..].Trim();

            return value.Length > 0 ? value : null;
        }

        return null;
    }
}
