namespace MediaMTX_Yuki_GUI.Models;

using System.Text.Json.Serialization;

/// <summary>app.json 的完整结构，是「用户意图」的唯一真相源。</summary>
public sealed class AppConfig
{
    /// <summary>
    /// 这个配置文件是「哪个程序版本写出来的」，编码方式与 <see cref="ProgramVersion.Code"/> 完全一致
    /// （major * 1_000_000 + minor * 1_000 + patch，0.0.1 → 1 → 九位串 000000001）。
    /// <para>
    /// 它<b>不单独维护一套"配置结构版本"</b>——那样迟早与程序版本分叉。它唯一的作用就是
    /// 跟当前程序版本比大小：小了就说明是旧程序写的，交给升级链处理；升级链尾端会把
    /// 它保底同步成当前程序版本，于是任何配置文件跑完升级链都自动成为「新版本的配置文件」。
    /// </para>
    /// <para>
    /// 默认值保持 <c>0</c>：让全新配置也完整走一遍升级链（哪怕一个段都不进），
    /// 这样「全新安装」与「升级上来」走的是同一条路，结构不会长出两套。
    /// </para>
    /// </summary>
    public int ConfigVersion { get; set; }

    /// <summary>版本号的字符串形式，纯给人看（日志、关于页），不参与比较。</summary>
    public string AppVersion { get; set; } = "v0.0.2";

    public EngineOptions Engine { get; set; } = new();

    /// <summary>RTMP 之外的对外协议。</summary>
    public ProtocolOptions Protocols { get; set; } = new();

    /// <summary>容错与调参。</summary>
    public AdvancedOptions Advanced { get; set; } = new();

    /// <summary>可选的运维接口。</summary>
    public OpsOptions Ops { get; set; } = new();

    /// <summary>
    /// 推流鉴权总开关（高级模式）。关着时引擎写一条 any 全放行，等于不验。
    /// 简洁模式不看这个值，一律按全放行生成。
    /// </summary>
    public bool AuthEnabled { get; set; }

    /// <summary>
    /// 账号表。一张表管推流与观看：每条账号能推哪些源由输入源那边的勾选决定，
    /// 要不要观看权由 <see cref="AuthAccount.CanView"/> 决定。
    /// </summary>
    /// <remarks>
    /// 落盘键叫 <c>accountTable</c> 而不是 <c>accounts</c>：老配置里 <c>accounts</c> 那一段
    /// 是观看账号对象，这里换成数组会让 System.Text.Json 报
    /// <c>The JSON property name for 'AppConfig.accounts' collides with another property</c>，
    /// 程序连启动都过不去（类型元数据在反序列化时就构建了，跟配置里有没有那个键无关）。
    /// </remarks>
    [JsonPropertyName("accountTable")]
    public List<AuthAccount> Accounts { get; set; } = new();


    public UiOptions Ui { get; set; } = new();

    /// <summary>拉流源默认值（高级模式）。</summary>
    public PullOptions Pull { get; set; } = new();

    /// <summary>本地录制（高级模式）。</summary>
    public RecordOptions Record { get; set; } = new();

    /// <summary>路径兜底规则（高级模式）。</summary>
    public PathOptions Paths { get; set; } = new();


    /// <summary>连接治理细项（高级模式）。</summary>
    public GovernanceOptions Governance { get; set; } = new();

    public UpdateOptions Update { get; set; } = new();

    /// <summary>默认推流代理：平台被墙时才需要，国内平台一律直连最快。</summary>
    public ProxyOptions Proxy { get; set; } = new();

    /// <summary>输入源：每一路在 MediaMTX 里是一条 path。</summary>
    public List<InputSource> Inputs { get; set; } = new();

    /// <summary>输出目标：所有启用的输入都会扇出到这些目标。</summary>
    public List<ForwardTarget> Targets { get; set; } = new();

    /// <summary>调试开关，界面上不显示，只给开发和临时验证用。见 <see cref="DebugOptions"/>。</summary>
    public DebugOptions Debug { get; set; } = new();
}
