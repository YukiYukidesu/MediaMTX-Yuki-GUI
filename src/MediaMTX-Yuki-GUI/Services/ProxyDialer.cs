using System.Net;
using System.Net.Sockets;
using System.Text;
using MediaMTX_Yuki_GUI.Models;

namespace MediaMTX_Yuki_GUI.Services;

/// <summary>
/// 通过代理建一条到目标的 TCP 连接。
/// 支持三条路：直连、系统代理、手动地址；协议只认 SOCKS5 与 HTTP CONNECT。
///
/// 这一层只管「把字节管道接起来」，不碰 RTMP、不碰 TLS 握手——
/// 谁来加密由上层决定，代理只负责把裸 TCP 送到目的地。
/// </summary>
public static class ProxyDialer
{
    /// <summary>连接代理本身的超时。</summary>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    /// <summary>握手阶段的总超时。</summary>
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(10);

    // ====== 对外入口 ======

    /// <summary>
    /// 连到 host:port，返回一条已就绪的流。
    /// 代理配置无效时直接抛异常，由调用方决定是提示用户还是静默重试。
    /// </summary>
    /// <param name="host">目标主机名或 IP。</param>
    /// <param name="port">目标端口。</param>
    /// <param name="options">代理设置；null 视为直连。</param>
    /// <param name="ct">取消令牌。</param>
    public static async Task<Stream> ConnectAsync(string host, int port, ProxyOptions? options, CancellationToken ct = default)
    {
        ProxyEndpoint endpoint = Resolve(options);

        if (endpoint.Kind == ProxyKind.Direct) return await ConnectDirectAsync(host, port, ct).ConfigureAwait(false);

        TcpClient client = await ConnectTcpAsync(endpoint.Host, endpoint.Port, ct).ConfigureAwait(false);

        try
        {
            NetworkStream stream = client.GetStream();

            if (endpoint.Kind == ProxyKind.Socks5)
            {
                await Socks5HandshakeAsync(stream, endpoint, host, port, options?.ResolveDnsRemotely ?? false, ct).ConfigureAwait(false);
            }
            else
            {
                await HttpConnectAsync(stream, endpoint, host, port, ct).ConfigureAwait(false);
            }

            return stream;
        }
        catch (Exception)
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 把配置折成一个具体端点。系统代理里读不到地址就回落直连；
    /// 手动档留空时也按系统代理走——设置页没法拦住用户不填，
    /// 这时候让这一档变成「用系统的」比悄悄改成直连更符合他看到的提示。
    /// </summary>
    public static ProxyEndpoint Resolve(ProxyOptions? options)
    {
        if (options is null) return ProxyEndpoint.Direct;

        string mode = options.Mode;

        if (mode == ProxyOptions.Direct) return ProxyEndpoint.Direct;

        if (mode == ProxyOptions.System || (mode == ProxyOptions.Manual && string.IsNullOrWhiteSpace(options.Url)))
        {
            return ResolveSystem();
        }

        if (mode == ProxyOptions.Manual) return Parse(options.Url) ?? ProxyEndpoint.Direct;

        // 目标级「跟随默认」在别处已经折算成具体档位，能走到这里说明配置没填全
        return ProxyEndpoint.Direct;
    }

    /// <summary>读系统代理；读不到或认不出就直连。</summary>
    private static ProxyEndpoint ResolveSystem()
    {
        string? systemUrl = SystemProxy.Read();

        if (string.IsNullOrWhiteSpace(systemUrl)) return ProxyEndpoint.Direct;

        return Parse(systemUrl) ?? ProxyEndpoint.Direct;
    }

    /// <summary>解析代理地址；识别不出主机与端口返回 null。</summary>
    public static ProxyEndpoint? Parse(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        string text = url.Trim();

        if (!text.Contains("://", StringComparison.Ordinal)) text = "http://" + text;

        if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri)) return null;

        ProxyKind kind = uri.Scheme.ToLowerInvariant() switch
        {
            "socks5" or "socks5h" or "socks" => ProxyKind.Socks5,
            _ => ProxyKind.Http,
        };

        int port = uri.Port;

        if (port <= 0) port = kind == ProxyKind.Socks5 ? 1080 : 8080;

        string user = string.Empty;
        string password = string.Empty;

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            int split = uri.UserInfo.IndexOf(':');

            if (split > 0)
            {
                user = Uri.UnescapeDataString(uri.UserInfo[..split]);
                password = Uri.UnescapeDataString(uri.UserInfo[(split + 1)..]);
            }
            else
            {
                user = Uri.UnescapeDataString(uri.UserInfo);
            }
        }

        return new ProxyEndpoint(kind, uri.Host, port, user, password);
    }

    // ====== 直连 ======

    private static async Task<Stream> ConnectDirectAsync(string host, int port, CancellationToken ct)
    {
        TcpClient client = await ConnectTcpAsync(host, port, ct).ConfigureAwait(false);

        return client.GetStream();
    }

    private static async Task<TcpClient> ConnectTcpAsync(string host, int port, CancellationToken ct)
    {
        TcpClient client = new() { NoDelay = true };

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ConnectTimeout);

        try
        {
            await client.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);

            return client;
        }
        catch (Exception)
        {
            client.Dispose();
            throw;
        }
    }

    // ====== SOCKS5 ======

    /// <summary>SOCKS5 握手（RFC 1928）+ 可选用户名密码认证（RFC 1929）+ CONNECT。</summary>
    private static async Task Socks5HandshakeAsync(
        NetworkStream stream,
        ProxyEndpoint endpoint,
        string host,
        int port,
        bool resolveRemotely,
        CancellationToken ct)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(HandshakeTimeout);

        bool needAuth = endpoint.User.Length > 0;
        byte[] greeting = needAuth ? new byte[] { 0x05, 0x02, 0x00, 0x02 } : new byte[] { 0x05, 0x01, 0x00 };

        await stream.WriteAsync(greeting, timeout.Token).ConfigureAwait(false);

        byte[] reply = new byte[2];

        await ReadExactlyAsync(stream, reply, timeout.Token).ConfigureAwait(false);

        if (reply[0] != 0x05) throw new IOException("代理不是 SOCKS5 服务");

        if (reply[1] == 0x02)
        {
            if (!needAuth) throw new IOException("代理要求用户名密码，但配置里没填");

            await Socks5AuthenticateAsync(stream, endpoint, timeout.Token).ConfigureAwait(false);
        }
        else if (reply[1] != 0x00)
        {
            throw new IOException("代理拒绝了全部认证方式");
        }

        byte[] request = BuildSocks5Connect(host, port, resolveRemotely);

        await stream.WriteAsync(request, timeout.Token).ConfigureAwait(false);

        byte[] head = new byte[4];

        await ReadExactlyAsync(stream, head, timeout.Token).ConfigureAwait(false);

        if (head[0] != 0x05) throw new IOException("SOCKS5 应答版本不对");
        if (head[1] != 0x00) throw new IOException("SOCKS5 连接失败，错误码 " + head[1].ToString("X2"));

        await SkipSocks5BindAddressAsync(stream, head[3], timeout.Token).ConfigureAwait(false);
    }

    /// <summary>吃掉应答里回的绑定地址，不关心内容。</summary>
    private static async Task SkipSocks5BindAddressAsync(NetworkStream stream, byte addressType, CancellationToken ct)
    {
        switch (addressType)
        {
            case 0x01:
            {
                byte[] skip = new byte[4 + 2];
                await ReadExactlyAsync(stream, skip, ct).ConfigureAwait(false);
                break;
            }
            case 0x04:
            {
                byte[] skip = new byte[16 + 2];
                await ReadExactlyAsync(stream, skip, ct).ConfigureAwait(false);
                break;
            }
            case 0x03:
            {
                byte[] length = new byte[1];
                await ReadExactlyAsync(stream, length, ct).ConfigureAwait(false);

                byte[] skip = new byte[length[0] + 2];
                await ReadExactlyAsync(stream, skip, ct).ConfigureAwait(false);
                break;
            }
            default: throw new IOException("SOCKS5 应答地址类型不认识");
        }
    }

    private static async Task Socks5AuthenticateAsync(NetworkStream stream, ProxyEndpoint endpoint, CancellationToken ct)
    {
        byte[] user = Encoding.UTF8.GetBytes(endpoint.User);
        byte[] password = Encoding.UTF8.GetBytes(endpoint.Password);

        if (user.Length > 255 || password.Length > 255) throw new IOException("代理用户名或密码过长");

        byte[] packet = new byte[3 + user.Length + password.Length];

        packet[0] = 0x01;
        packet[1] = (byte)user.Length;
        user.CopyTo(packet, 2);

        int passIndex = 2 + user.Length;

        packet[passIndex] = (byte)password.Length;
        password.CopyTo(packet, passIndex + 1);

        await stream.WriteAsync(packet, ct).ConfigureAwait(false);

        byte[] reply = new byte[2];

        await ReadExactlyAsync(stream, reply, ct).ConfigureAwait(false);

        if (reply[1] != 0x00) throw new IOException("代理用户名或密码不对");
    }

    private static byte[] BuildSocks5Connect(string host, int port, bool resolveRemotely)
    {
        List<byte> packet = new() { 0x05, 0x01, 0x00 };

        bool isIp = IPAddress.TryParse(host, out IPAddress? parsed);

        if (isIp && parsed!.AddressFamily == AddressFamily.InterNetwork)
        {
            packet.Add(0x01);
            packet.AddRange(parsed.GetAddressBytes());
        }
        else if (isIp && parsed!.AddressFamily == AddressFamily.InterNetworkV6)
        {
            packet.Add(0x04);
            packet.AddRange(parsed.GetAddressBytes());
        }
        else if (resolveRemotely)
        {
            // 域名交给代理去解析，本地 DNS 被污染时靠它
            byte[] name = Encoding.UTF8.GetBytes(host);

            packet.Add(0x03);
            packet.Add((byte)name.Length);
            packet.AddRange(name);
        }
        else
        {
            IPAddress[] addresses = Dns.GetHostAddresses(host);

            IPAddress picked = addresses.FirstOrDefault(item => item.AddressFamily == AddressFamily.InterNetwork)
                ?? addresses.FirstOrDefault()
                ?? throw new IOException("域名解析失败：" + host);

            packet.Add(picked.AddressFamily == AddressFamily.InterNetwork ? (byte)0x01 : (byte)0x04);
            packet.AddRange(picked.GetAddressBytes());
        }

        packet.Add((byte)(port >> 8));
        packet.Add((byte)(port & 0xFF));

        return packet.ToArray();
    }

    // ====== HTTP CONNECT ======

    private static async Task HttpConnectAsync(
        NetworkStream stream,
        ProxyEndpoint endpoint,
        string host,
        int port,
        CancellationToken ct)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(HandshakeTimeout);

        StringBuilder builder = new();

        builder.Append("CONNECT ").Append(host).Append(':').Append(port).Append(" HTTP/1.1\r\n");
        builder.Append("Host: ").Append(host).Append(':').Append(port).Append("\r\n");
        builder.Append("Proxy-Connection: keep-alive\r\n");

        if (endpoint.User.Length > 0)
        {
            string pair = endpoint.User + ":" + endpoint.Password;
            string token = Convert.ToBase64String(Encoding.UTF8.GetBytes(pair));

            builder.Append("Proxy-Authorization: Basic ").Append(token).Append("\r\n");
        }

        builder.Append("\r\n");

        byte[] request = Encoding.ASCII.GetBytes(builder.ToString());

        await stream.WriteAsync(request, timeout.Token).ConfigureAwait(false);

        string statusLine = await ReadStatusLineAsync(stream, timeout.Token).ConfigureAwait(false);

        // 形如 HTTP/1.1 200 Connection established
        string[] parts = statusLine.Split(' ', 3);

        if (parts.Length < 2) throw new IOException("代理响应看不懂：" + statusLine);
        if (parts[1] != "200") throw new IOException("代理拒绝连接：" + statusLine);

        // 状态行之后还有一个空行，吃掉它，别让它污染后续数据
        await ReadLineAsync(stream, timeout.Token).ConfigureAwait(false);
    }

    /// <summary>读一行（读到 LF 为止），去掉行尾的 CR。</summary>
    private static async Task<string> ReadLineAsync(NetworkStream stream, CancellationToken ct)
    {
        StringBuilder line = new();
        byte[] single = new byte[1];

        while (true)
        {
            int read = await stream.ReadAsync(single, ct).ConfigureAwait(false);

            if (read <= 0) throw new IOException("代理提前断开");

            char ch = (char)single[0];

            if (ch == '\n') break;

            line.Append(ch);

            if (line.Length > 1024) throw new IOException("代理响应过长");
        }

        return line.ToString().TrimEnd('\r');
    }

    private static Task<string> ReadStatusLineAsync(NetworkStream stream, CancellationToken ct)
    {
        return ReadLineAsync(stream, ct);
    }

    // ====== 工具 ======

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int offset = 0;

        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct).ConfigureAwait(false);

            if (read <= 0) throw new IOException("代理提前断开");

            offset += read;
        }
    }
}

/// <summary>代理协议。</summary>
public enum ProxyKind
{
    /// <summary>不走代理。</summary>
    Direct,

    /// <summary>SOCKS5。</summary>
    Socks5,

    /// <summary>HTTP CONNECT 隧道。</summary>
    Http,
}

/// <summary>一条已经解析好的代理端点。</summary>
/// <param name="Kind">协议。</param>
/// <param name="Host">代理主机。</param>
/// <param name="Port">代理端口。</param>
/// <param name="User">用户名，空串表示不需要认证。</param>
/// <param name="Password">密码。</param>
public sealed record ProxyEndpoint(ProxyKind Kind, string Host, int Port, string User, string Password)
{
    /// <summary>直连端点。</summary>
    public static ProxyEndpoint Direct { get; } = new(ProxyKind.Direct, string.Empty, 0, string.Empty, string.Empty);
}
