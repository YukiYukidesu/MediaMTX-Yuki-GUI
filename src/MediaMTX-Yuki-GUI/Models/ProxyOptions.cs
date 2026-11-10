using System.Text.Json.Serialization;

namespace MediaMTX_Yuki_GUI.Models;

/// <summary>
/// 一条出站代理的设置：直连 / 系统代理 / 手动三档。
/// 推流与更新下载各存一份，互不干扰——用户完全可能「推流走代理、更新走系统代理」。
/// </summary>
public sealed class ProxyOptions
{
    /// <summary>直连，不走任何代理。</summary>
    public const string Direct = "direct";

    /// <summary>跟随 Windows 的系统代理设置（注册表 Internet Settings，其次环境变量）。</summary>
    public const string System = "system";

    /// <summary>手动填一条代理地址。</summary>
    public const string Manual = "manual";

    /// <summary>目标级专用：跟随设置页的「默认推流代理」。</summary>
    public const string Follow = "follow";

    /// <summary>档位：direct / system / manual。</summary>
    public string Mode { get; set; } = System;

    /// <summary>
    /// 手动模式的代理地址，形如 socks5://127.0.0.1:8888 或 http://127.0.0.1:8888。
    /// 不写协议头时按 http 处理。
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// 把域名解析交给代理侧完成（SOCKS5 的 ATYP=0x03，等同 socks5h 语义），默认开着。
    ///
    /// 默认开是因为本机 DNS 大概率被污染：实测 api.github.com 会解析出
    /// 127.103.0.49 和伪造的 IPv6，本地解析后再把 IP 交给代理，隧道能通但 TLS 必挂。
    /// 域名直接交给代理，解析发生在墙外，绕开这一整类问题。
    /// HTTP CONNECT 本来就传域名，不受这个开关影响。
    /// </summary>
    public bool ResolveDnsRemotely { get; set; } = true;

    /// <summary>是不是手动档。界面上只有手动档才允许编辑地址。</summary>
    [JsonIgnore]
    public bool IsManual => Mode == Manual;
}
