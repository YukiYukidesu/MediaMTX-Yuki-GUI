namespace MediaMTX_Yuki_GUI.Controls;

/// <summary>
/// 关系图节点的定位信息：坐标与尺寸全部由视图模型算好，画布照着摆。
/// 定义在控件层是为了让画布不反过来依赖视图模型。
/// </summary>
public interface IMapNode
{
    /// <summary>节点在位移表里的键：源是 "s:{id}"，目标是 "t:{id}"。</summary>
    string Key { get; }

    /// <summary>节点左上角的 X。</summary>
    double X { get; }

    /// <summary>节点左上角的 Y。</summary>
    double Y { get; }

    /// <summary>节点宽度。</summary>
    double Width { get; }

    /// <summary>节点高度。</summary>
    double Height { get; }
}

/// <summary>关系图一条连线的状态：决定线色与线型，不表达节点自己的状态。</summary>
public enum MapLinkState
{
    /// <summary>两端都在转、链路通着：绿实线。</summary>
    Active,

    /// <summary>两端都启用，此刻还没通上：橙虚线。</summary>
    Pending,

    /// <summary>目标那头在报错：红虚线。</summary>
    Error,

    /// <summary>有一端停用，或者引擎没在跑：灰虚线。</summary>
    Off,
}