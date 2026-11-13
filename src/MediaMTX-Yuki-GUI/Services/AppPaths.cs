using System.Globalization;
using System.IO;

namespace MediaMTX_Yuki_GUI.Services;

/// <summary>程序的目录布局，所有路径统一从这里取，避免各处硬编码。</summary>
public static class AppPaths
{
    /// <summary>程序所在目录，即 MediaMTX-Yuki-GUI.exe 的父目录。</summary>
    public static string Root => AppContext.BaseDirectory.TrimEnd('\\', '/');

    // ====== 引擎 ======

    public static string MediaMTXDir => Path.Combine(Root, "MediaMTX");

    public static string MediaMTXExe => Path.Combine(MediaMTXDir, "mediamtx.exe");

    /// <summary>
    /// 调试用的引擎路径覆盖，由 `debug.engineExe` 填进来，空串表示不用。
    /// 这是整个程序里唯一一处「配置说了算」的路径：别的路径都跟着程序走，只有它能被指到别处。
    /// </summary>
    public static string EngineExeOverride { get; set; } = "";

    /// <summary>真正要启动的引擎可执行文件。没设覆盖就是正常那份。</summary>
    public static string EngineExe
        => EngineExeOverride.Length > 0 ? EngineExeOverride : MediaMTXExe;

    /// <summary>引擎的工作目录：跟着 <see cref="EngineExe"/> 所在的文件夹走。</summary>
    public static string EngineDir
        => EngineExeOverride.Length > 0
               ? Path.GetDirectoryName(EngineExeOverride) ?? MediaMTXDir
               : MediaMTXDir;

    /// <summary>
    /// 把 `debug.engineExe` 的原始值解析成绝对路径：相对路径按程序根目录展开，空串原样返回。
    /// </summary>
    public static string ResolveEngineExe(string? raw)
    {
        string value = (raw ?? string.Empty).Trim();
        if (value.Length == 0) return string.Empty;

        return Path.IsPathRooted(value) ? Path.GetFullPath(value) : Path.GetFullPath(Path.Combine(Root, value));
    }

    /// <summary>记录当前安装的 MediaMTX 版本与下载时间。</summary>
    public static string VersionFile => Path.Combine(MediaMTXDir, "version.json");

    // ====== 配置 ======

    public static string ConfigDir => Path.Combine(Root, "config");

    /// <summary>用户意图的唯一真相源。</summary>
    public static string AppJson => Path.Combine(ConfigDir, "app.json");

    /// <summary>每次启动与每次变更都重新生成的运行时配置。</summary>
    public static string RuntimeYml => Path.Combine(ConfigDir, "runtime.yml");

    // ====== 日志 ======

    public static string LogsDir => Path.Combine(Root, "logs");

    /// <summary>
    /// 引擎日志按天分文件：logs\mediamtx-yyyyMMdd.log。
    /// <para>
    /// 单文件是追加写的，天天被写就永远不会「变旧」，保留天数那套清理对它就等于没开；
    /// 按天分文件之后旧日志才真的会旧下去，设置页里的保留天数也才有意义。
    /// </para>
    /// </summary>
    public static string EngineLog => EngineLogFor(DateTime.Now);

    /// <summary>某一天该写进哪个日志文件。</summary>
    /// <param name="date">日期。</param>
    public static string EngineLogFor(DateTime date)
        => Path.Combine(LogsDir, "mediamtx-" + date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");

    // ====== 自更新 ======

    /// <summary>
    /// 本程序更新时下载的安装包与解出来的新 exe 放这儿。放在 config 下面而不是
    /// %TEMP%，是因为替换脚本要跨进程读它——临时目录可能被清理，也可能被沙箱拦。
    /// </summary>
    public static string UpdateDir => Path.Combine(ConfigDir, "update");

    /// <summary>界面上列的那四种目录之外的第五种：本程序自己的 exe 所在处。</summary>
    public static string AppExe => Path.Combine(Root, "MediaMTX-Yuki-GUI.exe");

    // ====== 初始化 ======

    /// <summary>确保运行期需要的目录都存在。</summary>
    public static void EnsureCreated()
    {
        Directory.CreateDirectory(ConfigDir);
        Directory.CreateDirectory(LogsDir);
    }
}
