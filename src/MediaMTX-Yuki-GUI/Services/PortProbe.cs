using System.Net;
using System.Net.Sockets;

namespace MediaMTX_Yuki_GUI.Services;

/// <summary>
/// 端口占用探测。用户自己填端口时容易和别的程序撞车，
/// 填完先探一下，撞了就当场提示，别等 MediaMTX 启动失败再回来找原因。
/// </summary>
public static class PortProbe
{
    /// <summary>
    /// 端口空闲返回 true。
    /// <paramref name="exempt"/> 里列出「本程序自己的引擎正在用的端口」，
    /// 它们当然绑不上，但那不叫冲突。
    /// </summary>
    public static bool IsFree(int port, IReadOnlyCollection<int>? exempt = null)
    {
        if (port is <= 0 or > 65535)
        {
            return false;
        }

        if (exempt != null && exempt.Contains(port))
        {
            return true;
        }

        TcpListener? listener = null;

        try
        {
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();

            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            try
            {
                listener?.Stop();
            }
            catch (Exception)
            {
                // 停不掉也没关系，进程退出会释放
            }
        }
    }

    /// <summary>取一个从 <paramref name="start"/> 开始、往上加能找到的空闲端口。</summary>
    public static int FindFree(int start, IReadOnlyCollection<int>? exempt = null)
    {
        for (int port = Math.Max(1024, start); port < 65535; port++)
        {
            if (IsFree(port, exempt))
            {
                return port;
            }
        }

        return start;
    }
}
