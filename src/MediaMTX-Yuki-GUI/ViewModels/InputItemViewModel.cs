using CommunityToolkit.Mvvm.ComponentModel;
using MediaMTX_Yuki_GUI.Controls;
using MediaMTX_Yuki_GUI.Models;

namespace MediaMTX_Yuki_GUI.ViewModels;

using L = MediaMTX_Yuki_GUI.Services.Localization;

/// <summary>输入源列表里的一行。每一行对应 MediaMTX 的一条 path。</summary>
public partial class InputItemViewModel : ObservableObject
{
    public InputItemViewModel(InputSource source)
    {
        Id = source.Id;
        _name = source.Name;
        _path = source.Path;
        _protocol = source.Protocol;
        _port = source.Port;
        _enabled = source.Enabled;
        _sourceUrl = source.SourceUrl;
        _record = source.Record;
        PublishAccountIds = new List<string>(source.PublishAccountIds);
    }

    public string Id { get; }

    /// <summary>
    /// 允许往这条 path 推流的账号 Id。空表示不限制——任何已启用的账号都能推，
    /// 老配置迁过来就是空，不会突然推不上去。
    /// </summary>
    public List<string> PublishAccountIds { get; }

    /// <summary>列表与下拉里显示的短 Id：前 6 位加省略号，完整值放 ToolTip。</summary>
    public string ShortId => ShortenId(Id);

    /// <summary>把 Id 压成「前 6 位 + …」；Id 本来就短时原样返回。</summary>
    /// <param name="id">完整 Id。</param>
    internal static string ShortenId(string? id)
    {
        string text = id ?? string.Empty;

        return text.Length <= 6 ? text : text[..6] + "…";
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string _name = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    [NotifyPropertyChangedFor(nameof(PushUrl))]
    private string _path = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProtocolText))]
    [NotifyPropertyChangedFor(nameof(ProtocolVariant))]
    [NotifyPropertyChangedFor(nameof(PushUrl))]
    [NotifyPropertyChangedFor(nameof(PortText))]
    private string _protocol = InputProtocols.Rtmp;

    /// <summary>该协议监听的端口；0 表示用协议默认端口。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PushUrl))]
    [NotifyPropertyChangedFor(nameof(PortText))]
    private int _port;

    /// <summary>
    /// 拉流地址。填了就让引擎主动去连这个地址，这条路径不再接收推流；
    /// 留空则由推流软件接入。两者互斥——MediaMTX 的 source 一次只能填一个值。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPulling))]
    [NotifyPropertyChangedFor(nameof(PushOrPullUrl))]
    [NotifyPropertyChangedFor(nameof(SourceText))]
    [NotifyPropertyChangedFor(nameof(SourceVariant))]
    private string _sourceUrl = "";

    /// <summary>
    /// 是否为这条输入录一份到本地。录制属于高级模式能力，
    /// 简洁模式下界面上不出现这个开关。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRecording))]
    [NotifyPropertyChangedFor(nameof(RecordStateText))]
    [NotifyPropertyChangedFor(nameof(RecordStateVariant))]
    private bool _record;

    /// <summary>总览页用：这条输入推到了哪些输出。</summary>
    public System.Collections.ObjectModel.ObservableCollection<TargetItemViewModel> Outputs { get; } = new();

    /// <summary>列表勾选，只活在界面上，不写进配置文件。</summary>
    [ObservableProperty]
    private bool _isSelected;

    // ====== 运行时状态（由主视图模型轮询引擎 API 后回填） ======

    /// <summary>列表「流速率」列：这一路此刻的入流码率，没在推时是破折号。</summary>
    [ObservableProperty]
    private string _inboundSpeedText = "—";

    /// <summary>列表「录制占用」列：这条 path 的录制目录占了多少磁盘，没录时是破折号。</summary>
    [ObservableProperty]
    private string _recordSizeText = "—";

    /// <summary>这条路径现在有没有在推流。停用的输入源引擎里根本没有这条路径，恒为 false。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LiveText))]
    [NotifyPropertyChangedFor(nameof(LiveVariant))]
    private bool _isOnline;

    /// <summary>总览页第二行的说明：编码、入流码率与读端数量；没在推流时是一句提示。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRuntimeText))]
    private string _runtimeText = string.Empty;

    /// <summary>没内容时把那一行整行收起来，不要留一条空行。</summary>
    public bool HasRuntimeText => RuntimeText.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveEnabled))]
    [NotifyPropertyChangedFor(nameof(IsRecording))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(StatusVariant))]
    [NotifyPropertyChangedFor(nameof(LiveText))]
    [NotifyPropertyChangedFor(nameof(LiveVariant))]
    [NotifyPropertyChangedFor(nameof(RecordStateText))]
    [NotifyPropertyChangedFor(nameof(RecordStateVariant))]
    private bool _enabled;

    /// <summary>
    /// 程序强制不用这一路，目前只有简洁模式下的主动拉流源。
    /// 与「用户自己关掉」分开显示：这一档画灰，用户关掉画红。
    /// 它是推导出来的运行时标记，不进配置文件。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveEnabled))]
    [NotifyPropertyChangedFor(nameof(IsRecording))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(StatusVariant))]
    [NotifyPropertyChangedFor(nameof(LiveText))]
    [NotifyPropertyChangedFor(nameof(LiveVariant))]
    [NotifyPropertyChangedFor(nameof(RecordStateText))]
    [NotifyPropertyChangedFor(nameof(RecordStateVariant))]
    private bool _runtimeDisabled;

    /// <summary>这条输入的完整推流地址，改了协议 / 端口 / 路径就自己重算。</summary>
    public string PushUrl
        => InputProtocols.BuildPushUrl(Protocol, Port, Path);

    public string DisplayName
        => string.IsNullOrWhiteSpace(Name) ? Path : Name;

    /// <summary>这条输入是主动去拉，还是接收推流。</summary>
    public bool IsPulling
        => SourceUrl.Length > 0;

    /// <summary>列表地址栏显示的那一串：拉流的显示拉流地址，等推的显示本机推流地址。</summary>
    public string PushOrPullUrl
        => IsPulling ? SourceUrl : PushUrl;

    public string ProtocolText
        => InputProtocols.DisplayName(Protocol);

    /// <summary>界面上显示的端口，填 0 时显示默认值。</summary>
    public string PortText
        => (Port > 0 ? Port : InputProtocols.DefaultPort(Protocol)).ToString();

    /// <summary>这一路实际会不会被引擎用到：用户开着，且程序没强制禁用。</summary>
    public bool EffectiveEnabled
        => Enabled && !RuntimeDisabled;

    /// <summary>
    /// 列表行里那颗录制按钮要不要亮成黄底。
    /// 光看 Record 不够：用户把整路停掉之后这条 path 根本不写进 runtime.yml，
    /// 录制键也就跟着失效，这时候还亮着会误让人以为正在录。
    /// </summary>
    public bool IsRecording
        => Record && EffectiveEnabled;

    /// <summary>
    /// 列表「录制状态」列。三档跟那三颗按钮的语义对齐：
    /// 没开录制是灰的，开了但整路停着是红的（这时录制键根本不写进 runtime.yml），真在录才是黄的。
    /// </summary>
    public string RecordStateText
        => !Record ? L.T("c003-042")
           : !EffectiveEnabled ? L.T("c003-025")
           : L.T("c003-041");

    public PillVariant RecordStateVariant
        => !Record ? PillVariant.Plain
           : !EffectiveEnabled ? PillVariant.Off
           : PillVariant.Warn;

    /// <summary>列表里的状态文案：程序禁用说明原因，否则看用户开关。</summary>
    public string StatusText
        => RuntimeDisabled ? L.T("c003-026")
           : Enabled ? L.T("c003-024") : L.T("c003-025");

    // ====== 胶囊标签（列表每行把各字段摆成一颗胶囊） ======

    /// <summary>状态胶囊配色：启用绿、用户停用红、程序禁用灰。</summary>
    public PillVariant StatusVariant
        => RuntimeDisabled ? PillVariant.Plain
           : Enabled ? PillVariant.On : PillVariant.Off;

    /// <summary>协议胶囊的配色，三种接入协议各占一色，便于扫一眼分清。</summary>
    public PillVariant ProtocolVariant
        => Protocol switch
        {
            InputProtocols.Rtsp => PillVariant.Rtsp,
            InputProtocols.Srt  => PillVariant.Srt,
            _                   => PillVariant.Rtmp,
        };

    /// <summary>推流状态胶囊的文案：被强制禁用说明原因，否则在线说在推流、关掉说已停止。</summary>
    public string LiveText
        => RuntimeDisabled ? L.T("c003-026")
           : !Enabled ? L.T("c003-025")
           : IsOnline ? L.T("c003-028") : L.T("c003-027");

    /// <summary>推流状态胶囊的配色，与文案同步：在线取绿，关掉取红，禁用与闲置取中性。</summary>
    public PillVariant LiveVariant
        => IsOnline && EffectiveEnabled ? PillVariant.On
           : RuntimeDisabled ? PillVariant.Plain
           : !Enabled ? PillVariant.Off : PillVariant.Plain;

    /// <summary>来源胶囊的文案：主动去拉，还是接收推流。</summary>
    public string SourceText
        => IsPulling ? L.T("c003-029") : L.T("c003-030");

    /// <summary>来源胶囊的配色：拉流借「跟随」那一支的天蓝，接收推流用中性灰。</summary>
    public PillVariant SourceVariant
        => IsPulling ? PillVariant.Follow : PillVariant.Plain;

    public InputSource ToModel()
        => new()
        {
            Id = Id,
            Name = Name,
            Path = Path,
            Protocol = Protocol,
            Port = Port,
            Enabled = Enabled,
            SourceUrl = SourceUrl.Trim(),
            Record = Record,
            PublishAccountIds = new List<string>(PublishAccountIds),
        };

    public static InputItemViewModel FromModel(InputSource source)
        => new(source);
}
