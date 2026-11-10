namespace MediaMTX_Yuki_GUI.ViewModels;

/// <summary>下拉里的一条输入源选项；Id 为空表示「未指定」。</summary>
public sealed record InputChoice(string Id, string Name);
