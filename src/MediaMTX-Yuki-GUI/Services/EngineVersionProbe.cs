using System.Diagnostics;
using System.IO;

namespace MediaMTX_Yuki_GUI.Services;

/// <summary>读取引擎自己报告的版本号。</summary>
public static class EngineVersionProbe
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    /// <summary>跑一次 <c>mediamtx.exe --version</c>，拿到类似 v1.21.3 的字符串；失败返回 null。</summary>
    public static async Task<string?> ReadAsync(string exePath, CancellationToken ct = default)
    {
        if (!File.Exists(exePath)) return null;

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);

        Process? process = null;

        try
        {
            process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = "--version",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };

            process.Start();

            string output = await process.StandardOutput
                .ReadToEndAsync(timeout.Token)
                .ConfigureAwait(false);

            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);

            string text = output.Trim();

            return string.IsNullOrEmpty(text) ? null : text;
        }
        catch (Exception)
        {
            // 探测失败不该影响启动，界面显示「未知」即可
            return null;
        }
        finally
        {
            if (process != null)
            {
                Kill(process);
                process.Dispose();
            }
        }
    }

    /// <summary>探测进程超时未退时直接杀掉，别留成孤儿。</summary>
    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // 进程可能刚好自己退了，杀掉失败无所谓
        }
    }
}
