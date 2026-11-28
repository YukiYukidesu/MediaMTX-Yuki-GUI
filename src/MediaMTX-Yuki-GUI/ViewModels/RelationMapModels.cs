using CommunityToolkit.Mvvm.ComponentModel;
using MediaMTX_Yuki_GUI.Controls;

namespace MediaMTX_Yuki_GUI.ViewModels;

/// <summary>关系图节点当前该用哪一档颜色。未启用的两档同时把图标换成叉。</summary>
public enum NodeState
{
    /// <summary>程序托管禁用：灰底灰字，叉。</summary>
    Plain,

    /// <summary>手动停用：红底红字，叉。</summary>
    Off,

    /// <summary>启用且就绪：绿底绿字，圆点。</summary>
    On,

    /// <summary>启用着但还没在推（待机），或孤儿目标：蓝底蓝字，圆点。</summary>
    Idle,

    /// <summary>出错：红底红字，圆点。</summary>
    Error,
}

/// <summary>关系图上的一个节点，源与目标共用这一套字段。</summary>
/// <remarks>
/// 坐标与宽度由主视图模型算好（<see cref="IMapNode"/>），这里只负责把名字与状态摆出来。
/// 节点就是一颗胶囊：前面一个图标、后面一个名字，底色与文字色由这几个状态开关决定，
/// 指标轮询回填时界面上的颜色自己会跟着变。
/// </remarks>
public sealed partial class RelationNode : ObservableObject, IMapNode
{
    public RelationNode(string key, bool isSource)
    {
        Key = key;
        IsSource = isSource;
    }

    /// <summary>节点在图里的唯一键：源是 "s:{id}"，目标是 "t:{id}"，位移表拿它当索引。</summary>
    public string Key { get; }

    /// <summary>是不是左侧那一列的源节点。</summary>
    public bool IsSource { get; }

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    /// <summary>胶囊里的名字：源用备注名，目标用目标名（没取名时回退平台名）。</summary>
    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>胶囊用哪一档语义色：直接交给 PillBadge，和列表页那颗胶囊完全同一套。</summary>
    [ObservableProperty]
    private PillVariant _variant = PillVariant.Plain;

    /// <summary>启用且就绪：绿底绿字。</summary>
    [ObservableProperty]
    private bool _isOn;

    /// <summary>启用着但还没在推（待机）：蓝底蓝字。</summary>
    [ObservableProperty]
    private bool _isIdle;

    /// <summary>出错：红底红字，图标仍是圆点。</summary>
    [ObservableProperty]
    private bool _isError;

    /// <summary>手动停用：红底红字，图标是叉。</summary>
    [ObservableProperty]
    private bool _isOff;

    /// <summary>悬停提示：协议、地址、运行状态这些放不下的话都塞这里。</summary>
    [ObservableProperty]
    private string _tip = string.Empty;
}