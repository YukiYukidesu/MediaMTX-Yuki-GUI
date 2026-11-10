namespace MediaMTX_Yuki_GUI.Services;

/// <summary>
/// 开机自启：往当前用户的 Run 键写一条启动命令，登录 Windows 后自动打开本程序。
/// </summary>
/// <remarks>
/// 没有用 Microsoft.Win32.Registry：本项目的 TFM 是 net10.0 而不是 net10.0-windows，
/// 那个程序集在这种目标框架下不可用，而本机的 NuGet restore 又坏了，临时加不了包。
/// 改调 reg.exe 效果完全一样，而且 regedit 里看到的就是同一条。
/// 另外这一项只写 HKCU，永远不碰 HKLM，卸载程序时也不需要管理员权限。
/// </remarks>
public static class AutoStart
{
    /// <summary>Run 键里看到的条目名，也是任务管理器「启动应用」里显示的名字。</summary>
    public const string ValueName = "MediaMTX-Yuki-GUI";

    private const string RunKey = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run";

    // ====== 读 ======

    /// <summary>读出已登记的启动命令；没登记或读不到都返回 null。</summary>
    public static string? ReadCommand()
    {
        (int code, string output) = Reg.Run($"query \"{RunKey}\" /v {ValueName}");

        return code == 0 ? ExtractData(output) : null;
    }

    /// <summary>已登记的启动命令是否还指向当前这个 exe（程序搬过目录就会对不上）。</summary>
    public static bool PointsToCurrentProcess()
    {
        string? command = ReadCommand();
        string? exe = Environment.ProcessPath;

        if (command is null || string.IsNullOrEmpty(exe)) return false;

        // ReadCommand 只返回引号里的路径（参数已被剥掉），所以这里可以精确比。
        // 程序搬过目录时对不上，EnsureRegistered 会把新路径写回去。
        return string.Equals(command, exe, StringComparison.OrdinalIgnoreCase);
    }

    // ====== 写 ======

    /// <summary>写或删自启登记，返回是否成功。开启但要不出当前 exe 路径时直接返回 false。</summary>
    public static bool SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            (int deleteCode, _) = Reg.Run($"delete \"{RunKey}\" /v {ValueName} /f");

            // 关掉一个本来就没开的开关时 reg 也返回非 0，那种情况按成功算
            return deleteCode == 0 || ReadCommand() is null;
        }

        string? exe = Environment.ProcessPath;

        if (string.IsNullOrEmpty(exe)) return false;

        // 开机自启一律带 --tray：登录后只留托盘图标，不弹主窗口。
        // 路径里可能有空格，reg 的数据项必须自带一层引号；外层再转义一次交给命令行解析
        string data = "\"" + exe + "\" --tray";

        (int addCode, _) = Reg.Run($"add \"{RunKey}\" /v {ValueName} /t REG_SZ /d \"{data.Replace("\"", "\\\"")}\" /f");

        return addCode == 0;
    }

    /// <summary>配置说该自启，就校对一遍注册表；程序搬过目录时这里会把旧路径换掉。</summary>
    public static void EnsureRegistered()
    {
        if (PointsToCurrentProcess()) return;

        SetEnabled(true);
    }

    // ====== 内部 ======

    /// <summary>
    /// reg.exe 的输出一行是「    MediaMTX-Yuki-GUI    REG_SZ    "C:\...\xxx.exe"」，
    /// 取 REG_SZ 之后的内容并去掉引号即可。
    /// </summary>
    private static string? ExtractData(string output)
    {
        foreach (string line in output.Split('\n'))
        {
            if (!line.Contains(ValueName, StringComparison.OrdinalIgnoreCase)) continue;

            string[] parts = line.Split("REG_SZ", StringSplitOptions.None);

            if (parts.Length < 2) continue;

            string data = parts[^1].Trim();

            // 登记的命令形如 "C:\dir\app.exe" --tray：只取第一对引号里的路径。
            // 不能直接 Trim('"')——它只剥首尾，路径后半截的引号会留在中间，
            // 变成 C:\dir\app.exe" --tray，跟纯 exe 路径怎么比都对不上。
            if (data.StartsWith('"'))
            {
                int close = data.IndexOf('"', 1);

                if (close > 0) return data[1..close];
            }

            return data;
        }

        return null;
    }
}
