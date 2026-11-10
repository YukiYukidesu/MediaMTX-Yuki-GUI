using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using MediaMTX_Yuki_GUI.Models;

namespace MediaMTX_Yuki_GUI.Services;

/// <summary>
/// 给「查更新 / 下安装包」用的 HttpClient 工厂，出口可以挂代理。
///
/// 为什么不用 <see cref="WebProxy"/>：它只认 HTTP 代理，配不了 SOCKS5。
/// 这里统一走 <see cref="SocketsHttpHandler.ConnectCallback"/>——
/// 不管直连、SOCKS5 还是 HTTP CONNECT，都由 <see cref="ProxyDialer"/> 把裸 TCP 接起来，
/// 上层照常做 TLS，SNI 仍是真实域名。
/// </summary>
public sealed class NetClient : IDisposable
{
    private readonly Func<ProxyOptions?> _proxy;

    private string _signature = string.Empty;
    private string _downloadSignature = string.Empty;
    private HttpClient? _http;
    private HttpClient? _downloadHttp;

    /// <summary>用一个取代理配置的回调建客户端；配置变了会自动重建连接池。</summary>
    /// <param name="proxy">每次取当前代理设置，返回 null 表示直连。</param>
    public NetClient(Func<ProxyOptions?> proxy)
    {
        _proxy = proxy;
    }

    /// <summary>当前可用的 HttpClient。代理配置一变就换一个新的，旧的一起扔掉。</summary>
    public HttpClient Http
    {
        get
        {
            ProxyOptions? options = _proxy();
            string signature = Signature(options);

            if (_http is not null && string.Equals(signature, _signature, StringComparison.Ordinal)) return _http;

            _http?.Dispose();

            _http = Build(options, TimeSpan.FromSeconds(15));
            _signature = signature;

            return _http;
        }
    }

    /// <summary>
    /// 下载安装包专用。查询只要十几秒，下载却要几分钟，
    /// 共用同一个 15 秒超时的客户端会让大文件必然失败，所以这里不限单次时长——
    /// 什么时候放弃由调用方的取消令牌决定。
    /// </summary>
    public HttpClient DownloadHttp
    {
        get
        {
            ProxyOptions? options = _proxy();
            string signature = Signature(options);

            if (_downloadHttp is not null && string.Equals(signature, _downloadSignature, StringComparison.Ordinal)) return _downloadHttp;

            _downloadHttp?.Dispose();

            _downloadHttp = Build(options, Timeout.InfiniteTimeSpan);
            _downloadSignature = signature;

            return _downloadHttp;
        }
    }

    /// <summary>把当前这条通道的描述出来，用于日志与界面提示。</summary>
    public string Describe()
    {
        ProxyEndpoint endpoint = ProxyDialer.Resolve(_proxy());

        return endpoint.Kind switch
        {
            ProxyKind.Direct => "直连",
            ProxyKind.Socks5 => "SOCKS5 " + endpoint.Host + ":" + endpoint.Port,
            _ => "HTTP 代理 " + endpoint.Host + ":" + endpoint.Port,
        };
    }

    public void Dispose()
    {
        _http?.Dispose();
        _http = null;

        _downloadHttp?.Dispose();
        _downloadHttp = null;
    }

    // ====== 内部 ======

    private static string Signature(ProxyOptions? options)
    {
        if (options is null) return "direct";

        return options.Mode + "|" + options.Url + "|" + (options.ResolveDnsRemotely ? "1" : "0");
    }

    private static HttpClient Build(ProxyOptions? options, TimeSpan timeout)
    {
        SocketsHttpHandler handler = new()
        {
            // 隧道由我们自己建，别让 HttpClient 再套一层系统代理
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        };

        ProxyEndpoint endpoint = ProxyDialer.Resolve(options);

        if (endpoint.Kind != ProxyKind.Direct)
        {
            handler.ConnectCallback = async (context, ct) =>
                await ProxyDialer.ConnectAsync(context.DnsEndPoint.Host, context.DnsEndPoint.Port, options, ct)
                    .ConfigureAwait(false);
        }

        HttpClient client = new(handler, disposeHandler: true)
        {
            Timeout = timeout,
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd("MediaMTX-Yuki-GUI");

        return client;
    }
}
