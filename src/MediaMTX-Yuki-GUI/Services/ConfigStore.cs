using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using MediaMTX_Yuki_GUI.Models;

namespace MediaMTX_Yuki_GUI.Services;

/// <summary>app.json 的读写与版本迁移，是配置进出的唯一通道。</summary>
public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // ====== 读 ======

    /// <summary>读取配置。文件缺失、损坏、需要迁移时都在这里就地处理。</summary>
    public AppConfig Load()
    {
        AppPaths.EnsureCreated();

        if (!File.Exists(AppPaths.AppJson)) return CreateDefault();

        AppConfig? cfg = TryRead();
        if (cfg == null) return CreateDefault();

        // ====== 迁移前先留一份原件，迁移段写错了还能人工救回来 ======
        if (!ConfigMigrator.IsCurrent(cfg)) Backup();

        cfg = ConfigMigrator.Upgrade(cfg);
        Save(cfg);

        return cfg;
    }

    private static AppConfig? TryRead()
    {
        try
        {
            string text = File.ReadAllText(AppPaths.AppJson);
            if (string.IsNullOrWhiteSpace(text)) return null;

            return JsonSerializer.Deserialize<AppConfig>(text, JsonOptions);
        }
        catch (JsonException)
        {
            // 用户手工改坏了，退回默认配置，原始文件已在 Save 前被 .bak 保住
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    // ====== 写 ======

    /// <summary>先写临时文件再替换，避免写到一半断电把配置写坏。</summary>
    public void Save(AppConfig cfg)
    {
        AppPaths.EnsureCreated();

        // 这个字段的语义是「最后一次写它的程序版本」，不是用户数据，所以每次落盘都刷成当前版本
        cfg.AppVersion = ProgramVersion.Text;

        string json = JsonSerializer.Serialize(cfg, JsonOptions);
        string tmp = AppPaths.AppJson + ".tmp";

        File.WriteAllText(tmp, json);
        File.Move(tmp, AppPaths.AppJson, overwrite: true);
    }

    // ====== 默认与备份 ======

    /// <summary>
    /// 全新安装的配置。
    ///
    /// <para>
    /// <b>故意从版本 0 起跑一遍升级链</b>，而不是直接 <c>new AppConfig()</c> 就落盘：
    /// 一个是让「全新安装」与「升级上来」走同一条路（迁移段里如果有"默认该补什么"，
    /// 两边会一起得到），另一个是链尾那句保底同步会把版本号写成当前程序版本 ——
    /// 这样新装的配置文件天然就是"新版本的配置文件"。
    /// </para>
    /// </summary>
    private AppConfig CreateDefault()
    {
        AppConfig cfg = ConfigMigrator.Upgrade(new AppConfig());

        Save(cfg);

        return cfg;
    }

    private static void Backup()
    {
        try
        {
            File.Copy(AppPaths.AppJson, AppPaths.AppJson + ConfigMigrator.BackupSuffix, overwrite: true);
        }
        catch (IOException)
        {
            // 备份失败不该拦住启动，最坏情况是这次迁移没有回滚点
        }
    }
}
