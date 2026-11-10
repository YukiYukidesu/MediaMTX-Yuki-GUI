using System.Diagnostics;
using System.IO;
using MediaMTX_Yuki_GUI.Models;

namespace MediaMTX_Yuki_GUI.Services;

/// <summary>MediaMTX 子进程的生命周期管理。启动后不出现任何窗口。</summary>
public sealed class EngineManager : IDisposable
{
    private Process? _process;

    /// <summary>
    /// 调试用的引擎参数覆盖，由 `debug.engineArgs` 填进来。
    /// 空串时用正常的「runtime.yml 绝对路径」。只在 <see cref="AppPaths.EngineExeOverride"/> 非空时才该被赋值。
    /// </summary>
    public string ArgumentsOverride { get; set; } = string.Empty;

    /// <summary>引擎吐出一行输出时触发。注意回调在线程池线程上，UI 需自行切回主线程。</summary>
    public event Action<string>? LogLine;

    /// <summary>
    /// 引擎进程退出时触发，参数是退出码（拿不到时为 null）。
    /// 只报告「进程没了」这个事实，**要不要重起由调用方决定**——
    /// 用户主动停、更新引擎时换文件，都不该被当成意外。
    /// 回调在线程池线程上，UI 需自行切回主线程。
    /// </summary>
    public event Action<int?>? Exited;

    /// <summary>
    /// 本次停止是不是我们自己要停的。停止流程里先置 true，<see cref="Exited"/> 才不会烧成「意外退出」。
    /// 下次 <see cref="Start"/> 会清掉它。
    /// </summary>
    private bool _stopRequested;

    public bool IsRunning
    {
        get
        {
            if (_process == null) return false;

            try
            {
                return !_process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    // ====== 启动 ======

    /// <summary>按 runtime.yml 拉起引擎，不等 API 就绪——就绪探测交给调用方。</summary>
    public bool Start()
    {
        if (IsRunning) return true;

        // 新一轮启动：之前那次「是我们要停的」作废，之后任何退出都算意外
        _stopRequested = false;

        AppPaths.EnsureCreated();

        string exePath = AppPaths.EngineExe;

        if (!File.Exists(exePath))
        {
            LogLine?.Invoke("[GUI] 找不到引擎文件：" + exePath);
            return false;
        }

        string arguments = ArgumentsOverride.Length > 0
                               ? ArgumentsOverride
                               : "\"" + AppPaths.RuntimeYml + "\"";

        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName           = exePath,
            Arguments          = arguments,
            WorkingDirectory   = AppPaths.EngineDir,
            UseShellExecute    = false,
            CreateNoWindow     = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            WindowStyle        = ProcessWindowStyle.Hidden,
        };

        try
        {
            _process = new Process
            {
                StartInfo           = psi,
                EnableRaisingEvents = true,
            };

            _process.OutputDataReceived += OnDataReceived;
            _process.ErrorDataReceived  += OnDataReceived;
            _process.Exited             += OnExited;

            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            // 挂进 Job Object：GUI 被强杀时 Windows 会把引擎一起收走，
            // 不依赖 Dispose 有没有机会跑。
            if (!EngineJob.Attach(_process))
            {
                LogLine?.Invoke("[GUI] 没能把引擎挂进 Job Object，GUI 异常退出时它可能留在后台");
            }

            return true;
        }
        catch (Exception ex)
        {
            LogLine?.Invoke("[GUI] 引擎启动失败：" + ex.Message);

            _process?.Dispose();
            _process = null;

            return false;
        }
    }

    // ====== 停止 ======

    /// <summary>强制结束引擎进程，并等它真正退出。更新 exe 之前必须先调用。</summary>
    public async Task StopAsync(int timeoutMs = 5000)
    {
        Process? proc = _process;
        if (proc == null) return;

        // 主动停：先记账，别让 Exited 被当成引擎自己崩了
        _stopRequested = true;

        try
        {
            if (!proc.HasExited)
            {
                proc.Kill(entireProcessTree: true);

                await proc.WaitForExitAsync()
                          .WaitAsync(TimeSpan.FromMilliseconds(timeoutMs));
            }
        }
        catch (Exception)
        {
            // 进程已在退出途中，或超时未退——无论哪种都不该挡住后续流程
        }
        finally
        {
            try
            {
                proc.Dispose();
            }
            catch (Exception)
            {
                // 释放失败无影响
            }

            if (ReferenceEquals(_process, proc)) _process = null;
        }
    }

    /// <summary>重启：先停干净再起，避免两个实例抢同一个端口。</summary>
    public async Task<bool> RestartAsync()
    {
        await StopAsync();

        return Start();
    }

    // ====== 事件 ======

    private void OnDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (e.Data != null) LogLine?.Invoke(e.Data);
    }

    private void OnExited(object? sender, EventArgs e)
    {
        // 主动停的那次不报意外，日志也说清楚，免得看日志的人以为崩了
        if (_stopRequested)
        {
            LogLine?.Invoke("[GUI] 引擎进程已按请求停止");

            return;
        }

        int? exitCode = null;

        try
        {
            exitCode = (sender as Process)?.ExitCode;
        }
        catch (Exception)
        {
            // 进程对象已释放时拿不到退出码，不影响后续判断
        }

        LogLine?.Invoke(exitCode is null
            ? "[GUI] 引擎进程意外退出"
            : "[GUI] 引擎进程意外退出，退出码 " + exitCode);

        Exited?.Invoke(exitCode);
    }

    // ====== 释放 ======

    public void Dispose()
    {
        Process? proc = _process;
        _process = null;

        if (proc == null) return;

        try
        {
            // 程序退出时必须把子进程一起带走。这里同步等，是因为 Closed 事件是同步的、
            // 没地方 await；而漏掉这一步 mediamtx.exe 会永远留在后台占着 1935。
            if (!proc.HasExited)
            {
                proc.Kill(entireProcessTree: true);
                proc.WaitForExit(3000);
            }
        }
        catch (Exception)
        {
            // 进程已在退出途中，或超时未退——退出流程不能因此中断
        }

        try
        {
            proc.Dispose();
        }
        catch (Exception)
        {
            // 释放失败无影响
        }
    }
}
