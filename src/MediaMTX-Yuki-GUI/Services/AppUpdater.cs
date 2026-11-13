using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using MediaMTX_Yuki_GUI.Models;

namespace MediaMTX_Yuki_GUI.Services;

/// <summary>
/// 本程序自己的更新：把 release 里的 x64 包下下来，解出 exe 与语言包，
/// 起一个批处理等主进程退出后再替换。
/// </summary>
/// <remarks>
/// <para>
/// **为什么不能自己替换自己**：Windows 上正在运行的 exe 是独占写锁，
/// 进程自己没办法把自己的文件覆盖掉（File.Replace 会抛共享冲突）。
/// 唯一稳的路子是「把要做的事写成脚本 → 启动它 → 自己退出 → 由脚本替换 → 脚本再把自己删掉」。
/// </para>
/// <para>
/// 只下不带引擎的那个包（<c>_x64.zip</c>）：带引擎的那个（<c>_x64_With_MediaMTX.zip</c>）
/// 里除了同一个 exe，还塞了一份几十 MB 的 mediamtx.exe，更新自己时用不上。
/// 换引擎走的是 <see cref="EngineUpdater"/>，两条链路互不相干。
/// </para>
/// </remarks>
public sealed class AppUpdater
{
    /// <summary>下载好的安装包与解出来的文件，<see cref="Prepare"/> 之后才有意义。</summary>
    /// <param name="Tag">版本号，形如 v0.0.2-alpha。</param>
    /// <param name="ExePath">解出来的新 exe。</param>
    /// <param name="Languages">解出来的语言包：文件名 → 内容。</param>
    public sealed record Package(string Tag, string ExePath, IReadOnlyDictionary<string, string> Languages);

    private readonly NetClient _net;

    /// <summary>走哪条代理去下载，由外部注入（设置页里那句「更新下载代理」）。</summary>
    public AppUpdater(Func<ProxyOptions?>? proxy = null)
    {
        _net = new NetClient(proxy ?? (() => null));
    }

    /// <summary>资产名。打包脚本按这个命名，改一处两边都得改。</summary>
    /// <remarks>
    /// 打包脚本产出的名字只带版本号（<c>MediaMTX-Yuki-GUI_v0.0.2_x64.zip</c>），
    /// 而 release 的 tag 带预发布后缀（<c>v0.0.2-alpha</c>），所以这里先把后缀切掉。
    /// 不切的话拼出来是 <c>…_v0.0.2-alpha_x64.zip</c>，那个资产不存在，下载必 404。
    /// </remarks>
    public static string AssetName(string tag)
        => "MediaMTX-Yuki-GUI_" + VersionPart(tag) + "_x64.zip";

    /// <summary>把 tag 里的预发布后缀切掉：<c>v0.0.2-alpha</c> → <c>v0.0.2</c>。</summary>
    public static string VersionPart(string tag)
    {
        int dash = tag.IndexOf('-');

        return dash > 0 ? tag[..dash] : tag;
    }

    /// <summary>
    /// 下载并解出这个版本要替换的文件。失败时抛异常，调用方负责翻译成人话。
    /// </summary>
    /// <param name="tag">版本号。</param>
    /// <param name="sourceId">更新源 Id，见 <see cref="SourceCatalog"/>。</param>
    /// <param name="repo">owner/repo。</param>
    /// <param name="onProgress">下载进度回调。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<Package> DownloadAsync(
        string tag,
        string sourceId,
        string repo,
        string progressPrefix,
        Action<string>? onProgress = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(AppPaths.UpdateDir);

        SourceCatalog.UpdateSource source = SourceCatalog.Find(sourceId);

        string url = SourceCatalog.AssetUrl(source, repo, tag, AssetName(tag));

        string zipPath = Path.Combine(AppPaths.UpdateDir, AssetName(tag));

        onProgress?.Invoke(progressPrefix);

        await PackageDownloader.DownloadToFileAsync(
                _net.Http,
                url,
                zipPath,
                (done, total) => onProgress?.Invoke(FormatProgress(progressPrefix, done, total)),
                ct)
            .ConfigureAwait(false);

        return Extract(zipPath, tag);
    }

    /// <summary>
    /// 起替换脚本，然后由调用方退出程序。脚本会等主进程退出、覆盖 exe、重启。
    /// </summary>
    /// <returns>成功时返回 null，失败返回给人看的原因。</returns>
    public static string? Prepare(Package package)
    {
        string target = AppPaths.AppExe;

        string languages = Path.Combine(AppPaths.Root, "Languages");

        string backup = target + ".old";

        string script = Path.Combine(AppPaths.UpdateDir, "apply-update.cmd");

        try
        {
            File.WriteAllText(script, BuildScript(package, target, languages, backup), new UTF8Encoding(false));

            Process.Start(new ProcessStartInfo
            {
                FileName = script,
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return ex.Message;
        }
    }

    /// <summary>清掉上次更新留下的安装包与解出来的目录（更新失败时它们会一直躺着）。</summary>
    public static void SweepLeftovers()
    {
        try
        {
            if (!Directory.Exists(AppPaths.UpdateDir)) return;

            foreach (string file in Directory.GetFiles(AppPaths.UpdateDir))
            {
                // 脚本本身正在跑的时候删不掉，那正好——它跑完会自己删自己
                PackageDownloader.TryDelete(file);
            }
        }
        catch (Exception)
        {
            // 清不掉不影响启动
        }
    }

    // ====== 内部 ======

    /// <summary>进度文字：前缀 + 已下大小，知道总大小就再带上百分比。</summary>
    private static string FormatProgress(string prefix, long done, long total)
    {
        string size = (done / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB";

        if (total <= 0) return prefix + " " + size;

        int percent = (int)(done * 100 / total);

        return prefix + " " + size + " / " + (total / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture)
               + " MB（" + percent.ToString(CultureInfo.InvariantCulture) + "%）";
    }

    /// <summary>把 zip 里的 exe 与语言包解到 update 目录下。</summary>
    private static Package Extract(string zipPath, string tag)
    {
        string staging = Path.Combine(AppPaths.UpdateDir, tag);

        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);

        Directory.CreateDirectory(staging);

        List<string> languages = [];

        using (ZipArchive archive = ZipFile.OpenRead(zipPath))
        {
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (entry.Name.Length == 0) continue;

                string? name = Match(entry);

                if (name is null) continue;

                string destination = Path.Combine(staging, name);

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

                entry.ExtractToFile(destination, overwrite: true);

                if (name.StartsWith("Languages/", StringComparison.OrdinalIgnoreCase)) languages.Add(name);
            }
        }

        string exe = Path.Combine(staging, "MediaMTX-Yuki-GUI.exe");

        if (!File.Exists(exe)) throw new FileNotFoundException("安装包里没有 MediaMTX-Yuki-GUI.exe", exe);

        // 语言包按「文件名」记，运行期要拿它去比对/覆盖 Languages 目录里的同名文件
        Dictionary<string, string> map = [];

        foreach (string name in languages)
        {
            map[Path.GetFileName(name)] = Path.Combine(staging, name);
        }

        return new Package(tag, exe, map);
    }

    /// <summary>
    /// 只认 exe 与 Languages 下的 json，其余（README.txt、MediaMTX\ 里的引擎）一概不解。
    /// 返回的是「相对于包根」的路径，顶层那个版本号目录要剥掉——包内结构是
    /// <c>MediaMTX-Yuki-GUI_v0.0.2_x64/MediaMTX-Yuki-GUI.exe</c>。
    /// </summary>
    private static string? Match(ZipArchiveEntry entry)
    {
        string full = entry.FullName.Replace('\\', '/');

        int slash = full.IndexOf('/');

        // 没有顶层目录的包（手工压的）也认：那就当它是从根开始的
        string relative = slash >= 0 ? full[(slash + 1)..] : full;

        if (relative.Equals("MediaMTX-Yuki-GUI.exe", StringComparison.OrdinalIgnoreCase))
        {
            return "MediaMTX-Yuki-GUI.exe";
        }

        if (relative.StartsWith("Languages/", StringComparison.OrdinalIgnoreCase)
            && relative.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return relative;
        }

        return null;
    }

    /// <summary>
    /// 写替换脚本。它在主进程退出之后才动手，所以里面每步都要能单独失败而不至于
    /// 把程序搞成打不开的状态——覆盖之前先备份，覆盖失败就把备份挪回去。
    /// </summary>
    private static string BuildScript(Package package, string target, string languagesDir, string backup)
    {
        string Q(string path) => "\"" + path + "\"";

        StringBuilder sb = new();

        sb.AppendLine("@echo off");
        sb.AppendLine("chcp 65001 >nul");
        sb.AppendLine("setlocal");

        // 主进程没退干净时 exe 是写不进去的，最多等 60 秒
        sb.AppendLine("set /a tries=0");
        sb.AppendLine(":wait");
        sb.AppendLine("timeout /t 1 /nobreak >nul");
        sb.AppendLine("2>nul (>>" + Q(Path.Combine(AppPaths.UpdateDir, "probe.lock")) + " call ) && (");
        sb.AppendLine("  set /a tries+=1");
        sb.AppendLine("  if %tries% lss 60 goto wait");
        sb.AppendLine(")");

        sb.AppendLine("if exist " + Q(target) + " move /y " + Q(target) + " " + Q(backup) + " >nul 2>&1");
        sb.AppendLine("copy /y " + Q(package.ExePath) + " " + Q(target) + " >nul 2>&1");

        sb.AppendLine("if not exist " + Q(target) + " (");
        sb.AppendLine("  if exist " + Q(backup) + " move /y " + Q(backup) + " " + Q(target) + " >nul 2>&1");
        sb.AppendLine("  start \"\" " + Q(target));
        sb.AppendLine("  exit /b 1");
        sb.AppendLine(")");

        foreach ((string name, string path) in package.Languages)
        {
            sb.AppendLine("if not exist " + Q(languagesDir) + " mkdir " + Q(languagesDir) + " >nul 2>&1");
            sb.AppendLine("copy /y " + Q(path) + " " + Q(Path.Combine(languagesDir, name)) + " >nul 2>&1");
        }

        sb.AppendLine("del /q " + Q(backup) + " >nul 2>&1");
        sb.AppendLine("start \"\" " + Q(target));
        sb.AppendLine("rmdir /s /q " + Q(Path.Combine(AppPaths.UpdateDir, package.Tag)) + " >nul 2>&1");
        sb.AppendLine("del /q " + Q(Path.Combine(AppPaths.UpdateDir, AssetName(package.Tag))) + " >nul 2>&1");

        // 删自己这一步必须最后一行，且用 (goto) 让 cmd 先放手再删
        sb.AppendLine("(goto) 2>nul & del \"%~f0\"");

        return sb.ToString();
    }
}
