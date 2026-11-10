using System.IO;
using System.Text.Json;
using MediaMTX_Yuki_GUI.Models;

namespace MediaMTX_Yuki_GUI.Services;

/// <summary>
/// 引擎（MediaMTX）的更新：查最新版本、下 zip、核对 SHA256、取出 mediamtx.exe、替换旧文件。
///
/// 分成「准备」与「落盘」两步，是因为替换 exe 必须先停引擎——
/// 引擎还跑着的时候那个文件是锁住的，任何写入都会失败。
/// 所以下载与校验可以在引擎转流的同时慢慢做，真正动文件只占一瞬间。
/// </summary>
public sealed class EngineUpdater
{
    private const string Repo = "bluenviron/mediamtx";

    /// <summary>官方压缩包挨着放的校验清单，格式是 sha256sum 的标准输出。</summary>
    private const string ChecksumsAsset = "checksums.sha256";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly NetClient _net;

    /// <summary>用哪条通道下载，由外部注入（设置页里那句「更新下载代理」）。</summary>
    /// <param name="proxy">取当前代理设置的回调。</param>
    public EngineUpdater(Func<ProxyOptions?> proxy)
    {
        _net = new NetClient(proxy);
    }

    /// <summary>官方 Windows x64 包的名字，例如 mediamtx_v1.21.3_windows_amd64.zip。</summary>
    public static string AssetName(string tag)
        => "mediamtx_" + tag + "_windows_amd64.zip";

    /// <summary>已经下载好、等着落盘的引擎包。</summary>
    public sealed class Package
    {
        /// <summary>包对应的 tag，例如 v1.21.3。</summary>
        public required string Tag { get; init; }

        /// <summary>mediamtx.exe 的字节内容。</summary>
        public required byte[] Executable { get; init; }

        /// <summary>压缩包留在磁盘上的路径，落盘后可以删。</summary>
        public required string ArchivePath { get; init; }
    }

    /// <summary>
    /// 下载并校验最新版引擎包。整个过程不碰现有文件，失败了引擎照常跑。
    /// </summary>
    /// <param name="tag">目标版本，例如 v1.21.3。</param>
    /// <param name="sourceId">更新源 Id，见 <see cref="SourceCatalog"/>。</param>
    /// <param name="onProgress">进度文字，直接显示给用户。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<Package> DownloadAsync(
        string tag,
        string? sourceId,
        Action<string>? onProgress,
        CancellationToken ct)
    {
        SourceCatalog.UpdateSource source = SourceCatalog.Find(sourceId);
        string asset = AssetName(tag);

        Directory.CreateDirectory(AppPaths.MediaMTXDir);

        string archive = Path.Combine(AppPaths.MediaMTXDir, asset + ".download");

        try
        {
            onProgress?.Invoke("正在下载 " + asset + " …");

            await PackageDownloader.DownloadToFileAsync(
                    _net.DownloadHttp,
                    SourceCatalog.AssetUrl(source, Repo, tag, asset),
                    archive,
                    (done, total) => onProgress?.Invoke(FormatProgress("正在下载引擎包", done, total)),
                    ct)
                .ConfigureAwait(false);

            await VerifyAsync(source, tag, asset, archive, onProgress, ct).ConfigureAwait(false);

            onProgress?.Invoke("正在解压 mediamtx.exe …");

            byte[]? exe = PackageDownloader.Extract(archive, "mediamtx.exe");

            if (exe is null || exe.Length == 0) throw new InvalidOperationException("压缩包里没有 mediamtx.exe");

            return new Package
            {
                Tag = tag,
                Executable = exe,
                ArchivePath = archive,
            };
        }
        catch (Exception)
        {
            // 下载失败就把半截文件清掉，别让下次误以为已经有包了
            PackageDownloader.TryDelete(archive);
            throw;
        }
    }

    /// <summary>
    /// 把新引擎写到磁盘，旧的留成 .old。调用前必须已经停掉引擎，否则文件是锁的。
    /// </summary>
    /// <param name="package">下载好的包。</param>
    /// <returns>成功返回 null，失败返回给人看的原因。</returns>
    public static string? Apply(Package package)
    {
        Directory.CreateDirectory(AppPaths.MediaMTXDir);

        string backup = AppPaths.MediaMTXExe + ".old";

        string? error = PackageDownloader.ReplaceFile(AppPaths.MediaMTXExe, package.Executable, backup);

        if (error is not null) return error;

        WriteVersionFile(package.Tag);

        PackageDownloader.TryDelete(backup);
        PackageDownloader.TryDelete(package.ArchivePath);

        return null;
    }

    /// <summary>
    /// 清掉上一次更新留下的残渣：旧引擎备份与没下完的包。
    ///
    /// 替换成功时 <see cref="Apply"/> 本要顺手删掉备份，可那一刻杀软往往正扫着新写进去的 exe，
    /// 删除会失败，留下一个几十 MB 的 mediamtx.exe.old。改到启动时收尾，那时候没人占文件。
    /// </summary>
    public static void SweepLeftovers()
    {
        PackageDownloader.TryDelete(AppPaths.MediaMTXExe + ".old");

        try
        {
            if (!Directory.Exists(AppPaths.MediaMTXDir)) return;

            foreach (string file in Directory.EnumerateFiles(AppPaths.MediaMTXDir, "*.download"))
            {
                PackageDownloader.TryDelete(file);
            }
        }
        catch (Exception)
        {
            // 扫不干净不影响用，下次启动再扫
        }
    }

    /// <summary>把下载失败的原因整理成一句人话。</summary>
    public static string DescribeError(Exception ex)
        => ex switch
        {
            HttpRequestException => "下载失败，可能是网络不通或镜像不可用",
            TaskCanceledException => "下载超时",
            InvalidOperationException => ex.Message,
            IOException => "写文件失败：" + ex.Message,
            _ => ex.Message,
        };

    // ====== 内部 ======

    /// <summary>
    /// 核对 SHA256。校验清单拿不到就算了（有些镜像只代理压缩包本身），
    /// 能拿到就必须一致——包被截断或换了内容要在这里拦住。
    /// </summary>
    private async Task VerifyAsync(
        SourceCatalog.UpdateSource source,
        string tag,
        string asset,
        string archive,
        Action<string>? onProgress,
        CancellationToken ct)
    {
        onProgress?.Invoke("正在校验 SHA256 …");

        string expected;

        try
        {
            string manifest = await PackageDownloader
                .DownloadTextAsync(_net.Http, SourceCatalog.AssetUrl(source, Repo, tag, ChecksumsAsset), ct)
                .ConfigureAwait(false);

            string? found = PackageDownloader.FindChecksum(manifest, asset);

            if (found is null) return;

            expected = found;
        }
        catch (Exception)
        {
            // 清单拿不到就不校验，别因为一个校验文件把整个更新堵死
            return;
        }

        string actual = await PackageDownloader.ComputeSha256Async(archive, ct).ConfigureAwait(false);

        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("下载的引擎包校验失败（SHA256 不一致），可能没下完整");
        }
    }

    private static void WriteVersionFile(string tag)
    {
        try
        {
            EngineVersionFile record = new()
            {
                Version = tag,
                InstalledAt = DateTimeOffset.Now,
            };

            File.WriteAllText(AppPaths.VersionFile, JsonSerializer.Serialize(record, JsonOptions));
        }
        catch (Exception)
        {
            // 这份记录只是给人看的，写不进去不影响引擎
        }
    }

    private static string FormatProgress(string prefix, long done, long total)
    {
        string size = (done / 1048576.0).ToString("0.0") + " MB";

        if (total <= 0) return prefix + " " + size;

        int percent = (int)(done * 100 / total);

        return prefix + " " + size + " / " + (total / 1048576.0).ToString("0.0") + " MB（" + percent + "%）";
    }

    /// <summary>version.json 的结构：当前装的是哪个版本、什么时候装的。</summary>
    private sealed class EngineVersionFile
    {
        public string Version { get; set; } = string.Empty;
        public DateTimeOffset InstalledAt { get; set; }
    }
}
