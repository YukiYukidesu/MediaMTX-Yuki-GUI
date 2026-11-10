using System.Security.Cryptography;

namespace MediaMTX_Yuki_GUI.Services.Rtmp;

/// <summary>
/// RTMP 握手（Simple 模式）。
///
/// 流程是 C0 C1 → S0 S1 S2 → C2，一共三趟：
/// 1. 客户端发 C0（1 字节版本号，固定 3）+ C1（1536 字节）；
/// 2. 服务端回 S0 + S1 + S2，S2 是 C1 的复制品；
/// 3. 客户端再发 C2，也就是 S1 的复制品，握手结束。
///
/// 我们两头都当：对 mediamtx 装服务端，对平台装客户端。
/// 既有的推流器都走 Simple 模式，用不着复杂握手里的 DH 与加密。
/// </summary>
public static class RtmpHandshake
{
    /// <summary>RTMP 版本号，写死 3。</summary>
    public const byte Version = 3;

    /// <summary>握手包体长度，固定 1536 字节。</summary>
    public const int BlockSize = 1536;

    /// <summary>以服务端身份完成握手，返回时隧道里只剩正常的 RTMP 数据。</summary>
    /// <param name="stream">已连上的流。</param>
    /// <param name="ct">取消令牌。</param>
    public static async Task AcceptAsync(Stream stream, CancellationToken ct)
    {
        byte[] c0c1 = new byte[1 + BlockSize];

        await stream.ReadExactlyAsync(c0c1, ct).ConfigureAwait(false);

        if (c0c1[0] != Version) throw new IOException("客户端 RTMP 版本不是 3");

        byte[] s0s1s2 = new byte[1 + 2 * BlockSize];

        s0s1s2[0] = Version;

        // S1：时间戳 4 字节 + 全零 4 字节 + 随机 1528 字节
        RandomNumberGenerator.Fill(s0s1s2.AsSpan(1 + 8, BlockSize - 8));

        // S2：原样回抄 C1
        Array.Copy(c0c1, 1, s0s1s2, 1 + BlockSize, BlockSize);

        await stream.WriteAsync(s0s1s2, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);

        byte[] c2 = new byte[BlockSize];

        await stream.ReadExactlyAsync(c2, ct).ConfigureAwait(false);
    }

    /// <summary>以客户端身份完成握手。</summary>
    /// <param name="stream">已连上的流。</param>
    /// <param name="ct">取消令牌。</param>
    public static async Task ConnectAsync(Stream stream, CancellationToken ct)
    {
        byte[] c0c1 = new byte[1 + BlockSize];

        c0c1[0] = Version;

        // C1：时间戳保持 0，zero 区全零，后面填随机数
        RandomNumberGenerator.Fill(c0c1.AsSpan(1 + 8, BlockSize - 8));

        await stream.WriteAsync(c0c1, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);

        byte[] s0s1 = new byte[1 + BlockSize];

        await stream.ReadExactlyAsync(s0s1, ct).ConfigureAwait(false);

        if (s0s1[0] != Version) throw new IOException("服务端 RTMP 版本不是 3");

        byte[] s2 = new byte[BlockSize];

        await stream.ReadExactlyAsync(s2, ct).ConfigureAwait(false);

        // C2：回抄 S1
        byte[] c2 = new byte[BlockSize];

        Array.Copy(s0s1, 1, c2, 0, BlockSize);

        await stream.WriteAsync(c2, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }
}
