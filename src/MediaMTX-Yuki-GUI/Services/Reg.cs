using System.Diagnostics;
using System.IO;

namespace MediaMTX_Yuki_GUI.Services;

/// <summary>
/// 跑一次 reg.exe 并等它结束。开机自启与托盘图标都要写注册表，实现只有这一份。
/// </summary>
/// <remarks>
/// 没有用 Microsoft.Win32.Registry：本项目的 TFM 是 net10.0 而不是 net10.0-windows，
/// 那个程序集在这种目标框架下不可用，而本机的 NuGet restore 又坏了，临时加不了包。
/// </remarks>
internal static class Reg
{
    /// <summary>起一次 reg.exe，任何异常都当成失败返回，绝不让调用方崩掉。</summary>
    internal static (int Code, string Output) Run(string arguments, int timeoutMs = 3000)
    {
        ProcessStartInfo info = new()
        {
            FileName = Path.Combine(Environment.SystemDirectory, "reg.exe"),
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        try
        {
            using Process? process = Process.Start(info);

            if (process is null) return (-1, string.Empty);

            string output = process.StandardOutput.ReadToEnd();

            process.StandardError.ReadToEnd();

            process.WaitForExit(timeoutMs);

            return process.HasExited ? (process.ExitCode, output) : (-1, output);
        }
        catch (Exception)
        {
            return (-1, string.Empty);
        }
    }
}
