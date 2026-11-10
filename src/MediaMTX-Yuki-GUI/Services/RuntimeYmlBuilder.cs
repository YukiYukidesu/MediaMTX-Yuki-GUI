using System.Text;
using MediaMTX_Yuki_GUI.Models;

namespace MediaMTX_Yuki_GUI.Services;

/// <summary>
/// 把 app.json 编译成 MediaMTX 能读的 runtime.yml。
/// 每次启动、每次变更都整份重写，不做增量修改——重写比改错便宜。
/// </summary>
public static class RuntimeYmlBuilder
{
    /// <summary>本程序使用的路径名，推流地址固定是 rtmp://127.0.0.1:1935/live。</summary>
    public const string PathName = "live";

    /// <summary>把配置编译成 runtime.yml 全文。</summary>
    /// <param name="cfg">配置。</param>
    /// <param name="destResolver">
    /// 把一条目标折算成写进 forward 的地址。默认直接用目标自己的地址；
    /// 需要走本机代理时传 <see cref="RtmpProxyServer.BuildLocalDest"/> 那一路进来。
    /// </param>
    public static string Build(AppConfig cfg, Func<ForwardTarget, string>? destResolver = null)
    {
        EngineOptions   engine    = cfg.Engine    ?? new EngineOptions();
        ProtocolOptions protocols = cfg.Protocols ?? new ProtocolOptions();
        AdvancedOptions advanced  = cfg.Advanced  ?? new AdvancedOptions();
        OpsOptions      ops       = cfg.Ops       ?? new OpsOptions();

        // 简洁模式不读高级侧那几组：拉流源整条不写、录制键不写、鉴权按全放行走。
        // 配置本身一个字段都不动，切回高级模式再原样生效。
        bool advancedMode = string.Equals(
            cfg.Ui?.Mode, UiOptions.AdvancedMode, StringComparison.Ordinal);

        StringBuilder sb = new StringBuilder();

        AppendLogging(sb);
        AppendProtocols(sb, engine, protocols, cfg.Inputs, advancedMode);
        AppendTimeouts(sb, advanced);
        AppendOps(sb, engine, ops);
        AppendAuth(sb, cfg, advancedMode);
        AppendPaths(sb, cfg, advanced, advancedMode, destResolver ?? (target => target.BuildDest()));

        return sb.ToString();
    }

    // ====== 日志 ======

    /// <summary>
    /// 必须同时给 stdout：GUI 的日志面板读的是重定向的标准输出，
    /// 只写 file 会让面板永远是空的。
    /// </summary>
    private static void AppendLogging(StringBuilder sb)
    {
        sb.AppendLine("logLevel: info");
        sb.AppendLine("logDestinations: [stdout, file]");
        sb.AppendLine("logFile: '" + Escape(AppPaths.EngineLog) + "'");
        sb.AppendLine();
    }

    // ====== 六种对外协议 ======

    /// <summary>
    /// 推流入口只开「输入源里真的用到了」的协议：某条输入选 SRT，就开 SRT；
    /// RTMP 是默认入口，无论如何都开。端口用默认值，界面上不再暴露。
    /// HLS / WebRTC / MoQ 是给观众播放用的，本工具不做播放，一律关掉省资源。
    /// </summary>
    private static void AppendProtocols(
        StringBuilder sb,
        EngineOptions engine,
        ProtocolOptions protocols,
        List<InputSource>? inputs,
        bool advancedMode)
    {
        // 简洁模式把播放协议一律关掉：HLS / WebRTC / MoQ 完全不监听，RTSP / SRT 只留作推流入口。
        // 端口设置项在简洁模式也是藏起来的，所以这里不必读它们，直接按「没开」写。
        if (!advancedMode)
        {
            protocols = new ProtocolOptions();
        }
        // 一个协议只能监听一个端口，同协议的多条输入共用第一个启用项填的端口
        Dictionary<string, int> ports = new(StringComparer.OrdinalIgnoreCase);

        foreach (InputSource input in inputs ?? [])
        {
            if (!input.Enabled)
            {
                continue;
            }

            string protocol = input.Protocol ?? InputProtocols.Rtmp;

            if (!ports.ContainsKey(protocol))
            {
                ports[protocol] = input.EffectivePort;
            }
        }

        if (!ports.ContainsKey(InputProtocols.Rtmp))
        {
            ports[InputProtocols.Rtmp] = engine.RtmpPort;
        }

        sb.AppendLine("rtmp: yes");
        sb.AppendLine("rtmpAddress: :" + ports[InputProtocols.Rtmp]);

        // RTSP 与 SRT 既是入口也是出口：输入源用到就开，设置页里手动开了也开
        bool hasRtsp = ports.TryGetValue(InputProtocols.Rtsp, out int rtspPort) || protocols.Rtsp;
        if (!ports.ContainsKey(InputProtocols.Rtsp)) rtspPort = protocols.RtspPort;
        sb.AppendLine("rtsp: " + Bool(hasRtsp));
        if (hasRtsp) sb.AppendLine("rtspAddress: :" + rtspPort);

        bool hasSrt = ports.TryGetValue(InputProtocols.Srt, out int srtPort) || protocols.Srt;
        if (!ports.ContainsKey(InputProtocols.Srt)) srtPort = protocols.SrtPort;
        sb.AppendLine("srt: " + Bool(hasSrt));
        if (hasSrt) sb.AppendLine("srtAddress: :" + srtPort);

        // 这三个纯给观众播放用，高级模式里开了才开
        sb.AppendLine("hls: " + Bool(protocols.Hls));
        if (protocols.Hls) sb.AppendLine("hlsAddress: :" + protocols.HlsPort);

        sb.AppendLine("webrtc: " + Bool(protocols.Webrtc));
        if (protocols.Webrtc) sb.AppendLine("webrtcAddress: :" + protocols.WebrtcPort);

        sb.AppendLine("moq: " + Bool(protocols.Moq));
        if (protocols.Moq) sb.AppendLine("moqAddress: :" + protocols.MoqPort);

        sb.AppendLine();
    }

    // ====== 超时与队列 ======

    private static void AppendTimeouts(StringBuilder sb, AdvancedOptions advanced)
    {
        sb.AppendLine("readTimeout: " + advanced.ReadTimeoutSeconds + "s");
        sb.AppendLine("writeTimeout: " + advanced.WriteTimeoutSeconds + "s");
        sb.AppendLine("writeQueueSize: " + advanced.WriteQueueSize);
        sb.AppendLine();
    }

    // ====== 运维接口 ======

    /// <summary>API 与 metrics 只监听回环，不给局域网调用；pprof 与 playback 按需开启。</summary>
    private static void AppendOps(StringBuilder sb, EngineOptions engine, OpsOptions ops)
    {
        sb.AppendLine("api: yes");
        sb.AppendLine("apiAddress: 127.0.0.1:" + engine.ApiPort);
        sb.AppendLine();
        sb.AppendLine("metrics: yes");
        sb.AppendLine("metricsAddress: 127.0.0.1:" + engine.MetricsPort);
        sb.AppendLine();

        sb.AppendLine("pprof: " + Bool(ops.Pprof));
        if (ops.Pprof) sb.AppendLine("pprofAddress: 127.0.0.1:" + ops.PprofPort);

        sb.AppendLine("playback: " + Bool(ops.Playback));
        if (ops.Playback) sb.AppendLine("playbackAddress: 127.0.0.1:" + ops.PlaybackPort);

        sb.AppendLine();
    }

    // ====== 鉴权 ======

    /// <summary>
    /// 账号表的写法。三条规则：
    /// <list type="number">
    ///   <item>简洁模式、或高级模式下鉴权开关关着 → 一条 <c>any</c> 全放行，等于不验；
    ///     老配置迁过来没开鉴权也走这条，行为与老版本一致。</item>
    ///   <item>开了鉴权 → 每个可用账号一条 user，publish 权限按输入源那边的勾选展开成多条
    ///     <c>path</c>；某账号没被任何源勾上就不给它 publish。</item>
    ///   <item><c>any</c> 只留 api / metrics —— 少了这两条 Control API 与 metrics 会回 401，
    ///     本程序自己就连不上引擎了。</item>
    /// </list>
    /// </summary>
    private static void AppendAuth(StringBuilder sb, AppConfig cfg, bool advancedMode)
    {
        sb.AppendLine("authMethod: internal");
        sb.AppendLine("authInternalUsers:");

        List<AuthAccount> usable = advancedMode && cfg.AuthEnabled
            ? cfg.Accounts.FindAll(a => a.IsUsable)
            : [];

        if (usable.Count > 0)
        {
            List<InputSource> inputs = cfg.Inputs ?? [];

            foreach (AuthAccount account in usable)
            {
                // 勾选了这条账号的源。某条源一个账号都没勾时表示「不限制」，谁都能推它。
                List<InputSource> allowed = inputs.FindAll(
                    i => i.PublishAccountIds.Count == 0 || i.PublishAccountIds.Contains(account.Id));

                // 既推不了任何源、又不许看，这条账号在 yml 里就没有意义，整条跳过
                if (allowed.Count == 0 && !account.CanView)
                {
                    continue;
                }

                sb.AppendLine("  - user: '" + Escape(account.User) + "'");
                sb.AppendLine("    pass: '" + Escape(account.Password) + "'");
                sb.AppendLine("    permissions:");

                foreach (InputSource input in allowed)
                {
                    sb.AppendLine("      - action: publish");
                    sb.AppendLine("        path: '" + Escape(input.Path) + "'");
                }

                if (account.CanView)
                {
                    sb.AppendLine("      - action: read");
                    sb.AppendLine("      - action: playback");
                }
            }

            sb.AppendLine("  - user: any");
            sb.AppendLine("    permissions:");
            sb.AppendLine("      - action: api");
            sb.AppendLine("      - action: metrics");
        }
        else
        {
            sb.AppendLine("  - user: any");
            sb.AppendLine("    permissions:");
            sb.AppendLine("      - action: publish");
            sb.AppendLine("      - action: read");
            sb.AppendLine("      - action: playback");

            // 少了这两条，Control API 与 metrics 会回 401，本程序自己就连不上了
            sb.AppendLine("      - action: api");
            sb.AppendLine("      - action: metrics");
        }

        sb.AppendLine();
    }

    // ====== 路径 ======

    private static void AppendPaths(
        StringBuilder sb,
        AppConfig cfg,
        AdvancedOptions advanced,
        bool advancedMode,
        Func<ForwardTarget, string> destResolver)
    {
        PullOptions pull  = cfg.Pull   ?? new PullOptions();
        PathOptions paths = cfg.Paths  ?? new PathOptions();
        RecordOptions rec = cfg.Record ?? new RecordOptions();

        sb.AppendLine("pathDefaults:");
        sb.AppendLine("  source: publisher");
        sb.AppendLine("  maxReaders: " + advanced.MaxReaders);
        sb.AppendLine("  alwaysAvailable: " + Bool(advanced.AlwaysAvailable));
        sb.AppendLine("  overridePublisher: " + Bool(advanced.OverridePublisher));
        sb.AppendLine("  useAbsoluteTimestamp: " + Bool(paths.UseAbsoluteTimestamp));

        // 按需拉流只在填了拉流地址的路径上起作用，写进默认值不影响推流路径
        if (pull.SourceOnDemand)
        {
            sb.AppendLine("  sourceOnDemand: yes");
            sb.AppendLine("  sourceOnDemandStartTimeout: " + pull.StartTimeoutSeconds + "s");
            sb.AppendLine("  sourceOnDemandCloseAfter: " + pull.CloseAfterSeconds + "s");
        }

        sb.AppendLine();
        sb.AppendLine("paths:");

        foreach (InputSource input in cfg.Inputs)
        {
            // 停用的输入源不写进配置，MediaMTX 那边就没有这条 path，自然推不上来
            if (!input.Enabled) continue;

            // 简洁模式不读拉流源：整条 path 都不写，引擎那边就没有这一路。
            // 直接用输入源自己的 SourceUrl 判断，不看那几项按需拉流参数——
            // 那些参数在简洁模式下本来也没写进 runtime.yml。
            if (!advancedMode && !string.IsNullOrWhiteSpace(input.SourceUrl)) continue;

            sb.AppendLine("  " + input.Path + ":");

            // 填了拉流地址就让引擎主动去连，否则照旧接收推流
            string source = string.IsNullOrWhiteSpace(input.SourceUrl)
                ? "publisher"
                : "'" + Escape(input.SourceUrl.Trim()) + "'";

            sb.AppendLine("    source: " + source);

            // 录制是 per-path 的：只在这条输入自己开了录制时才写，
            // 写进 pathDefaults 会变成「所有路一起录」。
            // 简洁模式不读这条开关，但配置里留着，切回高级模式照旧生效。
            if (advancedMode && input.Record) AppendRecord(sb, rec);

            AppendForward(sb, cfg.Targets, input.Id, destResolver);
        }

        // 兜底路径：没登记的路径名也收，但不会转发——转发列表是按输入源写的
        if (paths.AllOthers)
        {
            sb.AppendLine();
            sb.AppendLine("  all_others:");
            sb.AppendLine("    source: publisher");
        }
    }

    // ====== 录制 ======

    /// <summary>
    /// 给这条 path 打开录制。五个参数是全局一份的（设置页录制组），
    /// record 本身按 path 写，所以「哪几路录」由输入源自己的开关决定。
    /// </summary>
    private static void AppendRecord(StringBuilder sb, RecordOptions rec)
    {
        sb.AppendLine("    record: yes");
        // 模板里的相对前缀换成绝对路径：引擎的 cwd 是 MediaMTX 子目录，
        // 照相对路径算会录进 MediaMTX\recordings，用户按界面显示的目录找不到文件
        sb.AppendLine("    recordPath: '" + Escape(RecordPaths.Absolute(rec.PathTemplate)) + "'");
        sb.AppendLine("    recordFormat: " + rec.Format);
        sb.AppendLine("    recordSegmentDuration: " + rec.SegmentDuration);
        sb.AppendLine("    recordMaxPartSize: " + rec.MaxPartSize);
        sb.AppendLine("    recordDeleteAfter: " + rec.DeleteAfter);
    }

    /// <summary>只写启用的目标；一个都没有时不写 forward 段，MediaMTX 照常收流。</summary>
    private static void AppendForward(
        StringBuilder sb,
        List<ForwardTarget>? targets,
        string inputId,
        Func<ForwardTarget, string> destResolver)
    {
        // 一条输出只服务它绑定的那条输入，别的输入管道上不该出现它
        List<ForwardTarget> enabled = targets == null
            ? []
            : targets.Where(t => t.Enabled
                                 && string.Equals(t.InputSourceId, inputId, StringComparison.Ordinal)
                                 && destResolver(t).Length > 0).ToList();

        if (enabled.Count == 0) return;

        sb.AppendLine("    forward:");

        foreach (ForwardTarget target in enabled)
        {
            // # 在 YAML 里是注释起始符，必须整段加引号
            sb.AppendLine("      - dest: '" + Escape(destResolver(target)) + "'");
        }
    }

    // ====== 转义 ======

    /// <summary>YAML 单引号字符串里，单引号本身要写成两个。</summary>
    private static string Escape(string text)
        => text.Replace("'", "''");

    private static string Bool(bool value)
        => value ? "yes" : "no";
}
