using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MediaMTX_Yuki_GUI.Services;

/// <summary>
/// 把引擎进程挂进一个 Job Object，并设成「最后一个句柄关掉时整组一起死」。
/// 目的是兜住 GUI 被任务管理器强杀 / 崩溃这种走不到 Dispose 的情况：
/// 那时没人去 Kill 子进程，mediamtx.exe 会变成孤儿一直占着 1935，
/// 下次启动引擎就会因为端口被自己上次留下的进程占着而失败。
/// 挂上 Job 之后，Windows 在 GUI 进程消失的瞬间就把整组进程一起收走。
/// </summary>
public static class EngineJob
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;

    private static IntPtr _job = IntPtr.Zero;

    /// <summary>确保 Job 已创建。失败返回 false，此时只能退回「靠 Dispose 里 Kill」那条路。</summary>
    public static bool EnsureCreated()
    {
        if (_job != IntPtr.Zero) return true;

        IntPtr job = CreateJobObject(IntPtr.Zero, null);

        if (job == IntPtr.Zero) return false;

        JOBOBJECT_EXTENDED_LIMIT_INFORMATION info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JobObjectLimitKillOnJobClose,
            },
        };

        int size = Marshal.SizeOf(info);

        IntPtr buffer = Marshal.AllocHGlobal(size);

        try
        {
            Marshal.StructureToPtr(info, buffer, fDeleteOld: false);

            if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, buffer, (uint)size))
            {
                CloseHandle(job);

                return false;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        _job = job;

        return true;
    }

    /// <summary>把刚起来的引擎进程塞进 Job。失败不要紧，记一行日志即可。</summary>
    public static bool Attach(Process process)
    {
        if (_job == IntPtr.Zero && !EnsureCreated()) return false;

        try
        {
            return AssignProcessToJobObject(_job, process.Handle);
        }
        catch (Exception)
        {
            return false;
        }
    }

    // ====== P/Invoke ======

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr securityAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
