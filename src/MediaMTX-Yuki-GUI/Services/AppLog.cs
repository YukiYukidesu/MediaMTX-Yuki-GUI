using System;
using System.Collections.Generic;
using System.IO;

namespace MediaMTX_Yuki_GUI.Services;

/// <summary>本程序自己的运行记录：启动失败、重启、退出这类不进引擎日志的事。</summary>
/// <remarks>
/// 引擎自己的输出由 <see cref="EngineManager"/> 转成事件交给界面，界面上那份是内存里的滚动记录；
/// 这里落盘的是「界面还没起来就出事了」那一段——渲染后端拉不起 GPU、配置读不动、重启失败之类。
/// 写失败一律吞掉：日志写不进去也不能反过来把程序搞崩。
/// 最近若干行同时留在内存里：日志页勾上「显示调试日志」时要先把历史补进去，不能只看新来的。
/// </remarks>
public static class AppLog
{
    /// <summary>内存里留多少行。日志页一屏几十行，300 行足够回溯刚才那一次卡顿。</summary>
    private const int RecentCapacity = 300;

    private static readonly object Gate = new();

    private static readonly Queue<string> Recent = new();

    /// <summary>每写进一行发一次。可能在后台线程上触发，订阅者自己负责切回界面线程。</summary>
    public static event Action<string>? Written;

    /// <summary>往 logs\app-yyyyMMdd.log 追加一行，同时记进内存里的近期缓冲。</summary>
    public static void Write(string message)
    {
        DateTime now = DateTime.Now;

        string line = now.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture)
            + "  " + message;

        string path = Path.Combine(
            AppPaths.LogsDir,
            "app-" + now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture) + ".log");

        try
        {
            lock (Gate)
            {
                AppPaths.EnsureCreated();

                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 日志都写不动了，没什么可做的；内存里那份照样留给界面
        }

        lock (Gate)
        {
            Recent.Enqueue(line);

            while (Recent.Count > RecentCapacity) Recent.Dequeue();
        }

        Written?.Invoke(line);
    }

    /// <summary>最近写过的若干行，按写入顺序。</summary>
    public static string[] Snapshot()
    {
        lock (Gate)
        {
            return [.. Recent];
        }
    }
}
