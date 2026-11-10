using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace MediaMTX_Yuki_GUI.Services;

/// <summary>
/// MediaMTX Control API 的薄封装，只覆盖本程序要用的几个动作：
/// 就绪探测、热插拔转发，以及路径与转发的实时状态。
/// </summary>
public sealed class ControlApi : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;

    /// <summary>metrics 客户端按端口复用。这个循环每两秒跑一次，每次新建等于自己给自己造垃圾。</summary>
    private HttpClient? _meter;

    private int _meterPort;

    public ControlApi(int apiPort)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri("http://127.0.0.1:" + apiPort),
            Timeout = TimeSpan.FromSeconds(5),
        };
    }

    // ====== 就绪探测 ======

    /// <summary>轮询到 API 能应答为止。引擎刚启动时端口还没起来，必须等。</summary>
    public async Task<bool> WaitReadyAsync(TimeSpan timeout, CancellationToken ct)
    {
        DateTime deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            if (await PingAsync(ct)) return true;

            await Task.Delay(300, ct);
        }

        return false;
    }

    private async Task<bool> PingAsync(CancellationToken ct)
    {
        try
        {
            using HttpResponseMessage resp = await _http.GetAsync("/v3/config/global/get", ct);

            return resp.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }

    // ====== 热插拔转发 ======

    /// <summary>
    /// 整体替换某条路径的 forward 数组。
    /// 实测：只有内容变化的那几条会被重建，正在推的流不受影响。
    /// 传空列表等于清空这条路径的转发。
    /// </summary>
    public async Task<bool> PatchForwardAsync(string pathName, IReadOnlyList<string> dests, CancellationToken ct)
    {
        string body = BuildForwardBody(dests);

        using StringContent content = new StringContent(body, Encoding.UTF8, "application/json");
        using HttpRequestMessage req = new HttpRequestMessage(
            new HttpMethod("PATCH"),
            "/v3/config/paths/patch/" + pathName)
        {
            Content = content,
        };

        try
        {
            using HttpResponseMessage resp = await _http.SendAsync(req, ct);

            return resp.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }

    private static string BuildForwardBody(IReadOnlyList<string> dests)
    {
        using MemoryStream ms = new MemoryStream();
        using (Utf8JsonWriter writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("forward");

            foreach (string dest in dests)
            {
                writer.WriteStartObject();
                writer.WriteString("dest", dest);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    // ====== 路径与转发的实时状态 ======

    /// <summary>拿所有路径的在线状态、编码与读端数量。</summary>
    /// <remarks>接口没有按名字过滤的参数，本程序的路径又只有几条，一次全取最省事。</remarks>
    public async Task<IReadOnlyList<PathInfo>> GetPathsAsync(CancellationToken ct)
    {
        PathListResponse? list = await GetJsonAsync<PathListResponse>(
            "/v3/paths/list?itemsPerPage=1000", ct);

        return list?.Items ?? [];
    }

    /// <summary>拿某条路径下每条转发的状态与失败原因。</summary>
    /// <param name="pathName">路径名，必须是引擎里真实存在的那条。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<IReadOnlyList<ForwardDestInfo>> GetForwardDestsAsync(string pathName, CancellationToken ct)
    {
        string url = "/v3/paths/forward-dests/list?path=" + Uri.EscapeDataString(pathName);

        ForwardDestListResponse? list = await GetJsonAsync<ForwardDestListResponse>(url, ct);

        return list?.Items ?? [];
    }

    // ====== 录像 ======

    /// <summary>列出全部 path 的录像段。引擎按录制目录扫盘，未在录的 path 也会出现在结果里。</summary>
    /// <param name="ct">取消令牌。</param>
    public async Task<IReadOnlyList<RecordingInfo>> GetRecordingsAsync(CancellationToken ct)
    {
        RecordingListResponse? list = await GetJsonAsync<RecordingListResponse>(
            "/v3/recordings/list?itemsPerPage=1000", ct);

        return list?.Items ?? [];
    }

    /// <summary>删掉一段录像。不可撤销，调用方必须先确认。</summary>
    /// <param name="pathName">path 名。</param>
    /// <param name="start">段开始时间，用接口返回的原文。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<bool> DeleteRecordingSegmentAsync(string pathName, string start, CancellationToken ct)
    {
        string url = "/v3/recordings/segments/delete?path=" + Uri.EscapeDataString(pathName)
                     + "&start=" + Uri.EscapeDataString(start);

        try
        {
            using HttpResponseMessage resp = await _http.DeleteAsync(url, ct);

            return resp.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }

    /// <summary>GET 一个 JSON 对象。连不上、404、格式变了都只是「这次没拿到」，不抛给调用方。</summary>
    private async Task<T?> GetJsonAsync<T>(string url, CancellationToken ct)
        where T : class
    {
        try
        {
            using HttpResponseMessage resp = await _http.GetAsync(url, ct);

            if (!resp.IsSuccessStatusCode) return null;

            await using Stream body = await resp.Content.ReadAsStreamAsync(ct);

            return await JsonSerializer.DeserializeAsync<T>(body, JsonOptions, ct);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ====== 码率统计 ======

    /// <summary>抓 Prometheus metrics 原文，调用方自己解析需要的行。</summary>
    public async Task<string?> GetMetricsAsync(CancellationToken ct, int metricsPort)
    {
        HttpClient meter = MeterFor(metricsPort);

        try
        {
            return await meter.GetStringAsync("/metrics", ct);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>设置页改了 metrics 端口就换一个客户端，端口没变则一直用同一个。</summary>
    private HttpClient MeterFor(int metricsPort)
    {
        if (_meter is not null && _meterPort == metricsPort) return _meter;

        _meter?.Dispose();

        _meter = new HttpClient
        {
            BaseAddress = new Uri("http://127.0.0.1:" + metricsPort),
            Timeout = TimeSpan.FromSeconds(3),
        };

        _meterPort = metricsPort;

        return _meter;
    }

    // ====== 释放 ======

    public void Dispose()
    {
        _http.Dispose();
        _meter?.Dispose();
    }
}
