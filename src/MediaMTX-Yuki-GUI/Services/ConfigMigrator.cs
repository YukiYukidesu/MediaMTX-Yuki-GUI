using MediaMTX_Yuki_GUI.Models;

namespace MediaMTX_Yuki_GUI.Services;

/// <summary>
/// 配置升级链。**它唯一要保证的事：跑完之后 <c>cfg.ConfigVersion</c> 等于当前程序版本。**
///
/// 设计要点（这是用户想清楚的设计，别改回去）：
/// <list type="bullet">
///   <item><b>不单独维护"配置结构版本"常量。</b>配置里那个数字只用来跟 <see cref="ProgramVersion.Code"/> 比大小；
///     另立一套号必然与程序版本分叉，分叉了就要有人手工对齐，迟早出错。</item>
///   <item><b>链尾无条件保底同步。</b>哪怕整条链一个 <c>if</c> 都没进去（程序升级了但配置结构没动），
///     末尾那句赋值也会把配置版本号拉到程序版本号——于是它自然就是"新版本的配置文件"了。</item>
///   <item><b>迁移段只在"配置结构真的变了"时才写。</b>程序版本怎么涨都不必写段。</item>
/// </list>
///
/// 铁律：已发布的迁移段永不修改，只在末尾追加；用 <c>&lt;</c> 判断而非 <c>==</c>，
/// 这样更老的配置会依次穿过所有需要的段，不必写 N×(N-1)/2 条组合路径。
/// </summary>
public static class ConfigMigrator
{
    /// <summary>迁移前的备份后缀，备份文件与原文件同目录。</summary>
    public const string BackupSuffix = ".bak";

    // ====== 入口 ======

    /// <summary>判断配置是否已经是当前程序版本，调用方据此决定要不要备份。</summary>
    public static bool IsCurrent(AppConfig cfg)
        => cfg.ConfigVersion >= ProgramVersion.Code;

    /// <summary>
    /// 按版本号从小到大的顺序穿过所有需要的迁移段，**最后无条件把版本号同步到程序版本**。
    /// </summary>
    public static AppConfig Upgrade(AppConfig cfg)
    {
        // 已经是最新，不必白跑一遍
        if (IsCurrent(cfg)) return cfg;

        // 配置比程序还新（用户装回了旧版程序），不认识的东西不能瞎改。
        // 注意这里连尾端同步也要跳过：把版本号"降级"写回去，等于替用户丢了信息。
        if (cfg.ConfigVersion > ProgramVersion.Code) return cfg;

        // ====== 迁移段区（按版本从小到大）======
        //
        // 目前仍是第一个版本，**一个迁移段都没有**。
        // 将来配置结构真的变了，就在这里追加，形状是：
        //
        //   if (cfg.ConfigVersion < 1001)   // 0.1.1 那次结构变化
        //   {
        //       cfg.NewSection ??= new NewSectionOptions();
        //       // 注意：段里**不**赋 cfg.ConfigVersion，统一交给链尾
        //   }
        //
        // 段内只改结构，版本号由链尾那句兜住——这样即使某段忘了赋值，也不会漏同步。

        // ====== 链尾保底：把配置版本号同步到当前程序版本 ======
        //
        // 这一句是整条链的收口。程序从 0.1.0 升到 0.1.1 但结构没变时，
        // 上面一段都不会进，配置版本号依然会在这里被拉到 0.1.1 ——
        // 于是配置文件就成了"新版本的配置文件"，不必每发一版都改一次这里。
        cfg.ConfigVersion = ProgramVersion.Code;

        return cfg;
    }
}