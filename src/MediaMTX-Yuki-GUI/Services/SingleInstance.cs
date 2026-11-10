using System.Threading;

namespace MediaMTX_Yuki_GUI.Services;

/// <summary>
/// 单实例守卫。同一登录会话里只允许一个本程序在跑：
/// 第二个实例启动时先给已在跑的那个发一次「唤出窗口」信号，然后自己立刻退出。
/// 名字用 Local\ 前缀，按登录会话隔离——多用户各开各的很正常，不该互相挤掉。
/// </summary>
public static class SingleInstance
{
    private const string MutexName = @"Local\MediaMTX-Yuki-GUI.SingleInstance";
    private const string ShowEventName = @"Local\MediaMTX-Yuki-GUI.ShowWindow";

    private static Mutex? _mutex;

    /// <summary>
    /// 放开单实例锁，供「重启程序」用。
    /// </summary>
    /// <remarks>
    /// 新进程起来时也要 TryAcquire，锁还在手里就会被判成第二个实例、直接退出。
    /// 先放锁再起新进程；中途失败也无所谓——旧进程马上就没了，锁跟着释放。
    /// </remarks>
    public static void Release()
    {
        Mutex? mutex = Interlocked.Exchange(ref _mutex, null);

        if (mutex is null)
        {
            return;
        }

        try
        {
            mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // 不是持有者（理论上不会发生），锁还是会随进程退出被系统清掉
        }

        mutex.Dispose();
    }

    /// <summary>抢到实例权返回 true；已有实例在跑返回 false（调用方应当直接退出）。</summary>
    public static bool TryAcquire()
    {
        Mutex mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);

        if (!createdNew)
        {
            mutex.Dispose();

            return false;
        }

        _mutex = mutex;

        return true;
    }

    /// <summary>第二个实例调用：叫醒已运行的那个，让它把窗口亮出来。</summary>
    public static void RequestShow()
    {
        try
        {
            // 第一个实例已经建好了同名事件，这里打开的就是它那个。
            // 万一没有（刚被关掉），这句会顺手新建一个，Set 完随即释放，不会有副作用。
            using EventWaitHandle handle = new(initialState: false, EventResetMode.AutoReset, ShowEventName);

            handle.Set();
        }
        catch (Exception)
        {
            // 叫不醒就算了，本实例本来就要退出
        }
    }

    /// <summary>第一个实例调用：起一条后台线程等信号，收到就执行 <paramref name="onShow"/>。</summary>
    public static void Listen(Action onShow)
    {
        EventWaitHandle handle;

        try
        {
            handle = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, ShowEventName);
        }
        catch (Exception)
        {
            return;
        }

        Thread thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    handle.WaitOne();
                    onShow();
                }
                catch (Exception)
                {
                    return;
                }
            }
        })
        {
            IsBackground = true,
            Name         = "single-instance-listener",
        };

        thread.Start();
    }
}
