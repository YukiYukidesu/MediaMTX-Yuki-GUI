using System;
using System.IO;
using System.Text.Json;
using MediaMTX_Yuki_GUI.Models;

namespace MediaMTX_Yuki_GUI.Services;

/// <summary>
/// 启动极早期就要用的那几项配置：渲染后端、窗口材质、多开开关之类。
/// </summary>
/// <remarks>
/// 这些值必须赶在 <see cref="Program.BuildAvaloniaApp"/> 之前拿到——Avalonia 一旦建好平台，
/// 渲染后端就定死了，再改只能重启。所以这里自己读一遍 app.json，把整份 AppConfig 缓存下来，
/// 让 <see cref="DebugSwitches"/> 与设置页都能共用同一份结果。
///
/// 读失败一律当默认值处理：配置坏了也要能启动，进去了再让用户改。
/// </remarks>
public static class EarlyConfig
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static AppConfig? _config;

    /// <summary>启动阶段读到的那份配置；还没读过就先读一次。</summary>
    public static AppConfig Config
    {
        get
        {
            if (_config is not null) return _config;

            try
            {
                string path = AppPaths.AppJson;
                if (File.Exists(path))
                {
                    string text = File.ReadAllText(path);
                    if (text.Length > 0)
                        _config = JsonSerializer.Deserialize<AppConfig>(text, Options);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // 读不动就当没配置，后面 Save 会把整份写回去
            }

            return _config ??= new AppConfig();
        }
    }

    /// <summary>界面偏好那一段，最常用的入口。</summary>
    public static UiOptions Ui => Config.Ui;

    /// <summary>把缓存换成最新的一份（设置页存盘后调用，免得下次重启读到旧的）。</summary>
    public static void Replace(AppConfig config) => _config = config;
}
