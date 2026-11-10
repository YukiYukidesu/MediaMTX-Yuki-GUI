using MediaMTX_Yuki_GUI.ViewModels;

namespace MediaMTX_Yuki_GUI.Services;

/// <summary>
/// ViewModel 需要的那几项界面能力。
/// WPF 时代这些直接调 Application.Current / Clipboard / Window.ShowDialog；
/// Avalonia 里窗口与弹窗的写法全变了，统一从这里走，VM 里一行 WPF 都不留。
/// </summary>
public interface IAppHost
{
    /// <summary>弹出输入源编辑窗，返回 true 表示用户按了保存。</summary>
    /// <param name="item">正在编辑的那一条。</param>
    /// <param name="isNew">true 表示这是新增，按钮上写「添加」。</param>
    /// <param name="ownPorts">自己已经在用的端口，用来避开端口冲突。</param>
    /// <param name="otherNames">其它条目已经在用的备注，用来提示重名。</param>
    /// <param name="otherPaths">其它条目已经在用的路径名，重了要拦住。</param>
    /// <param name="advancedMode">高级模式才露出录制开关。</param>
    /// <param name="accounts">可选的推流账号；空集合时整块不显示。</param>
    Task<bool> ShowInputEditorAsync(
        InputItemViewModel item,
        bool isNew,
        IReadOnlyCollection<int> ownPorts,
        IReadOnlyCollection<string> otherNames,
        IReadOnlyCollection<string> otherPaths,
        bool advancedMode,
        IReadOnlyList<AuthAccountItemViewModel> accounts);

    /// <summary>弹出输出目标编辑窗，返回 true 表示用户按了保存。</summary>
    /// <param name="item">正在编辑的那一条。</param>
    /// <param name="isNew">true 表示这是新增，按钮上写「添加」。</param>
    /// <param name="choices">可以绑定到的输入源下拉项。</param>
    /// <param name="otherNames">其它条目已经在用的备注，用来提示重名。</param>
    Task<bool> ShowTargetEditorAsync(
        TargetItemViewModel item,
        bool isNew,
        IReadOnlyList<InputChoice> choices,
        IReadOnlyCollection<string> otherNames);

    /// <summary>弹出账号编辑窗，返回 true 表示用户按了保存。</summary>
    /// <param name="item">正在编辑的那一条（编辑时是副本，取消就丢了）。</param>
    /// <param name="isNew">true 表示这是新增。</param>
    /// <param name="otherUsers">其它账号已经在用的用户名，重了要拦住。</param>
    Task<bool> ShowAccountEditorAsync(
        AuthAccountItemViewModel item,
        bool isNew,
        IReadOnlyCollection<string> otherUsers);

    /// <summary>弹出某一路的录像列表，用户能在这里删掉不要的段。</summary>
    /// <param name="pathName">path 名，也就是输入源的路径名。</param>
    /// <param name="displayName">备注，也用在窗口标题上。</param>
    /// <param name="inputId">这条输入的完整 Id，窗口里整条印出来供核对。</param>
    /// <param name="address">这条输入的完整地址：等推的印推流地址，主动拉的印拉流地址。</param>
    /// <param name="loader">读列表。返回 null 表示读不到（引擎没运行），空集合表示确实没录过。</param>
    /// <param name="deleter">删一段，返回 true 表示删掉了。</param>
    Task ShowRecordingsAsync(
        string pathName,
        string displayName,
        string inputId,
        string address,
        Func<CancellationToken, Task<IReadOnlyList<RecordingSegmentInfo>?>> loader,
        Func<string, string, CancellationToken, Task<bool>> deleter);

    /// <summary>弹一个确认窗，返回 true 表示用户点了确认。</summary>
    /// <param name="title">标题。</param>
    /// <param name="message">正文，讲清这么做的后果。</param>
    /// <param name="okText">确认按钮上的字。</param>
    /// <param name="destructive">true 表示这一下会删掉东西，按钮用红底。</param>
    Task<bool> ConfirmAsync(string title, string message, string okText, bool destructive = false);

    /// <summary>
    /// 按配置切窗口背景材质（mica / acrylic / solid）。
    /// 窗口是次抛的，重建之后由 <see cref="AppShell"/> 重新调一次。
    /// </summary>
    void ApplyBackdrop(string? mode);

    /// <summary>写系统剪贴板。</summary>
    Task CopyTextAsync(string text);

    /// <summary>
    /// 弹系统「另存为」对话框，把一段文字写到用户挑的位置。
    /// 返回 true 表示写出去了；用户取消、或没有权限写，都返回 false。
    /// </summary>
    /// <param name="suggestedName">默认文件名，不含路径。</param>
    /// <param name="content">要写的文本，按 UTF-8 落盘。</param>
    Task<bool> SaveTextFileAsync(string suggestedName, string content);

    /// <summary>把动作切回 UI 线程。</summary>
    void Post(Action action);
}
