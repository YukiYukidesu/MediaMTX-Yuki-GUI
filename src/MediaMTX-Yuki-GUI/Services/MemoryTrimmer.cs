using System.Runtime.InteropServices;

namespace MediaMTX_Yuki_GUI.Services;

/// <summary>
/// 空闲回收：界面关掉之后隔一会儿把托管堆收一遍，再把工作集还给系统。
/// 做法照 Filee 的 MemoryTrimmer 抄的——先等一段时间没人叫醒，确认不忙才动手，
/// 等待时长由设置页的档位决定（1 秒 / 10 秒 / 30 秒 / 不回收）。
/// 注意 <c>SetProcessWorkingSetSize</c> 只是把页面踢出工作集，提交量一点不降：
/// 任务管理器的「内存」列看的正是工作集，所以数字好看，但内存并没有真还给系统，
/// 下次用到还得缺页拉回来。真回收靠的是前面那两次 GC。
/// </summary>
public sealed class MemoryTrimmer : IDisposable
{
    /// <summary>默认档位：界面关掉之后等这么久才动手，期间又有人叫醒就重头等。</summary>
    public static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(30);

    private readonly object _gate = new();
    private readonly Func<bool> _busy;
    private readonly Action _trim;
    private readonly Timer _timer;

    private TimeSpan _delay = IdleDelay;

    private bool _enabled = true;
    private bool _requested;
    private bool _disposed;

    public MemoryTrimmer(Func<bool> busy, Action trim)
    {
        _busy  = busy;
        _trim  = trim;
        _timer = new Timer(_ => OnQuiet(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    // ====== 档位 ======

    /// <summary>
    /// 换档位。关掉的时候顺手把已经排上的那一次撤掉——
    /// 用户选了「不回收」，那就不该再偷偷跑一次。
    /// </summary>
    public void SetPolicy(bool enabled, TimeSpan delay)
    {
        lock (_gate)
        {
            if (_disposed) return;

            _enabled = enabled;
            _delay   = delay;

            if (!enabled)
            {
                _requested = false;

                _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

                return;
            }

            // 已经在等的那一次按新时长重排，不至于让用户觉得改了没反应
            if (_requested) _timer.Change(_delay, Timeout.InfiniteTimeSpan);
        }
    }

    // ====== 计时 ======

    /// <summary>界面刚关掉，或者又有人要用界面了：重头计时，到点再看忙不忙。</summary>
    public void Request()
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (!_enabled) return;

            _requested = true;

            _timer.Change(_delay, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnQuiet()
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (!_enabled)
            {
                _requested = false;

                return;
            }

            if (!_requested) return;

            // 等这会儿窗口又回来了，让路，下一轮再说
            if (_busy())
            {
                _timer.Change(_delay, Timeout.InfiniteTimeSpan);

                return;
            }

            _requested = false;
        }

        _trim();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;

            _timer.Dispose();
        }
    }

    // ====== 回收动作 ======

    /// <summary>收一遍托管堆，再把工作集交还系统。会阻塞几百毫秒，别在 UI 线程上直接调。</summary>
    public static void TrimAndRelease()
    {
        // 第一次只是把垃圾收掉，堆里空出来的区间还 commit 着；
        // Aggressive + compacting 才会把这些空区间真正 decommit 掉
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);

        ReleaseWorkingSet();
    }

    private static void ReleaseWorkingSet()
    {
        try
        {
            SetProcessWorkingSetSize(GetCurrentProcess(), -1, -1);
        }
        catch (Exception)
        {
            // 调不到就算了，上面那两次 GC 已经把大头做完了
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessWorkingSetSize(nint process, nint minimumSize, nint maximumSize);

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();
}
