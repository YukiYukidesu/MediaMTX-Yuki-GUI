using System;
using System.IO;

namespace MediaMTX_Yuki_GUI.Services;

/// <summary>本程序自己的运行记录：启动失败、重启、退出这类不进引擎日志的事。</summary>
/// <remarks>
/// 引擎自己的输出由 <see cref="EngineManager"/> 转成事件交给界面，界面上那份是内存里的滚动记录；
/// 这里落盘的是「界面还没起来就出事了」那一段——渲染后端拉不起 GPU、配置读不动、重启失败之类。
/// 写失败一律吞掉：日志写不进去也不能反过来把程序搞崩。
/// </remarks>
public static class AppLog
{
    private static readonly object Gate = new();

    /// <summary>往 logs\app-yyyyMMdd.log 追加一行。</summary>
    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                AppPaths.EnsureCreated();

                string path = Path.Combine(
                    AppPaths.LogsDir,
                    "app-" + DateTime.Now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture) + ".log");

                File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture)
                    + "  " + message + Environment.NewLine);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 日志都写不动了，没什么可做的
        }
    }
}
