namespace MediaMTX_Yuki_GUI.Services;


using L = MediaMTX_Yuki_GUI.Services.Localization;
/// <summary>界面文案的集中入口，按当前语言返回对应文字。</summary>
public static class Strings
{
    /// <summary>当前界面语言，由主视图模型在启动时写入。</summary>
    public static string Language { get; set; } = "zh-CN";

    /// <summary>当前是不是英文界面。</summary>
    private static bool IsEnglish
        => Language.StartsWith("en", StringComparison.OrdinalIgnoreCase);

    /// <summary>目标列表里「启停状态」列的文案。</summary>
    /// <param name="enabled">用户是否启用了这条转发。</param>
    /// <param name="runtimeState">引擎侧上报的原始状态，可能为空。</param>
    public static string TargetState(bool enabled, string runtimeState)
    {
        string key = !enabled
            ? "disabled"
            : string.IsNullOrEmpty(runtimeState) ? "unknown" : runtimeState;

        return key switch
        {
            "disabled"     => IsEnglish ? "Disabled" : L.T("l004-001"),
            "unknown"      => IsEnglish ? "Not running" : L.T("l004-002"),
            "running"      => IsEnglish ? "Streaming" : L.T("l004-003"),
            "forwarding"   => IsEnglish ? "Forwarding" : L.T("l004-003"),
            "idle"         => IsEnglish ? "Idle" : L.T("l004-004"),
            "ready"        => IsEnglish ? "Standby" : L.T("l004-005"),
            "reconnecting" => IsEnglish ? "Reconnecting" : L.T("l004-006"),
            "error"        => IsEnglish ? "Error" : L.T("l004-007"),
            _              => runtimeState,
        };
    }
}
