using System.IO;
using System.Text.Json;
using MediaMTX_Yuki_GUI.Models;

namespace MediaMTX_Yuki_GUI.Services;

/// <summary>
/// 在正式配置加载之前把 `debug` 段摸出来。
///
/// <see cref="ConfigStore.Load"/> 会做迁移、会落盘、还会刷版本号——那些都该等到
/// 界面层起来再做。但有两个开关偏偏要在那之前就知道：允许多开（单实例守卫在
/// `Main` 第一句就跑）和跳过解压缓存清理（缓存清理立刻跟着跑）。
///
/// 所以这里只做一件事：把 app.json 当纯 JSON 读一遍，取 `debug` 段。
/// 文件不存在、读不动、解析失败、字段缺——一律当「全用默认值」，绝不因此挡住启动。
/// </summary>
public static class DebugSwitches
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    // ====== 对外入口 ======

    /// <summary>读 `debug` 段，拿不到就给一份全默认的。</summary>
    public static DebugOptions Load()
    {
        try
        {
            if (!File.Exists(AppPaths.AppJson)) return new DebugOptions();

            string text = File.ReadAllText(AppPaths.AppJson);
            if (string.IsNullOrWhiteSpace(text)) return new DebugOptions();

            AppConfig? cfg = JsonSerializer.Deserialize<AppConfig>(text, Options);

            return cfg?.Debug ?? new DebugOptions();
        }
        catch (Exception)
        {
            // 配置坏了会由 ConfigStore 那边报错与重建，这里只负责不添乱
            return new DebugOptions();
        }
    }
}
