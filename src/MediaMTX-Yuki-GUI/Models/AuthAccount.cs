namespace MediaMTX_Yuki_GUI.Models;

using System.Text.Json.Serialization;

/// <summary>
/// 一条推流账号。MediaMTX 的 <c>authInternalUsers</c> 是全局一张表，但每条权限都能带
/// <c>path</c> 限定到具体路径，所以「谁只能推哪一路」是引擎原生支持的，不必自己造账本。
/// </summary>
public sealed class AuthAccount
{
    /// <summary>稳定标识：输入源绑定的是它，改用户名不会把绑定关系弄断。</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string User { get; set; } = "admin";

    public string Password { get; set; } = "";

    /// <summary>是否同时给观看权限（read + playback）。推流与观看不互斥，可以都占。</summary>
    public bool CanView { get; set; }

    /// <summary>用户名与密码都齐了才算这条账号可用。纯计算结果，不写进 app.json。</summary>
    [JsonIgnore]
    public bool IsUsable
        => !string.IsNullOrWhiteSpace(User) && !string.IsNullOrEmpty(Password);
}
