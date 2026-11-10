using System.IO;

namespace MediaMTX_Yuki_GUI.Services;

/// <summary>按保留天数清理 logs 目录里的旧日志。</summary>
/// <remarks>
/// 只在程序启动时跑一次，而且必须赶在引擎起来之前：引擎把当天那份日志一直握着写，
/// 运行中去删它既删不掉、也没必要——它本来就不是「旧」的那一份。
/// </remarks>
public static class LogCleaner
{
    /// <summary>认得出来的日志文件，天分文件与升级前留下的单文件都算。</summary>
    private const string LogPattern = "*.log";

    /// <summary>把最后写入时间早于保留期的日志删掉，返回删掉的个数。</summary>
    /// <param name="retentionDays">保留天数，0 表示不清理。</param>
    /// <param name="now">当前时间，留空取系统时间（测试时好固定基准）。</param>
    public static int Clean(int retentionDays, DateTime? now = null)
    {
        if (retentionDays <= 0) return 0;
        if (!Directory.Exists(AppPaths.LogsDir)) return 0;

        DateTime cutoff = (now ?? DateTime.Now).AddDays(-retentionDays);
        int removed = 0;

        foreach (string file in Directory.EnumerateFiles(AppPaths.LogsDir, LogPattern, SearchOption.TopDirectoryOnly))
        {
            try
            {
                if (File.GetLastWriteTime(file) >= cutoff) continue;

                File.Delete(file);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 被占用或没权限：跳过这一个，不能因为一份日志删不掉就中断整轮清理
            }
        }

        return removed;
    }
}
