using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

namespace MediaMTX_Yuki_GUI.Services;

/// <summary>
/// 下载安装包、核对哈希、从压缩包里取出文件。
/// 引擎更新与本程序更新共用这一套，区别只在包名与要取出的文件名。
/// </summary>
public static class PackageDownloader
{
    private const int BufferSize = 81920;

    /// <summary>把 URL 下到磁盘，边下边报进度（已下载字节、总字节，总字节未知时为 -1）。</summary>
    public static async Task DownloadToFileAsync(
        HttpClient http,
        string url,
        string path,
        Action<long, long>? onProgress,
        CancellationToken ct)
    {
        using HttpResponseMessage response = await http
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        long total = response.Content.Headers.ContentLength ?? -1;

        await using Stream remote = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using FileStream local = new(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            useAsync: true);

        byte[] buffer = new byte[BufferSize];
        long done = 0;

        while (true)
        {
            int read = await remote.ReadAsync(buffer, ct).ConfigureAwait(false);

            if (read <= 0) break;

            await local.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);

            done += read;
            onProgress?.Invoke(done, total);
        }
    }

    /// <summary>把一段文本下到内存里，用于只有几百字节的校验清单。</summary>
    public static async Task<string> DownloadTextAsync(HttpClient http, string url, CancellationToken ct)
    {
        using HttpResponseMessage response = await http.GetAsync(url, ct).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    /// <summary>算文件的 SHA256，小写十六进制。</summary>
    public static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using FileStream stream = File.OpenRead(path);

        byte[] hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// 从 sha256sum 清单里取某个文件名的哈希；找不到返回 null。
    /// 官方格式是「&lt;哈希&gt; *&lt;文件名&gt;」，星号表示二进制模式。
    /// </summary>
    public static string? FindChecksum(string manifest, string fileName)
    {
        foreach (string raw in manifest.Split('\n'))
        {
            string line = raw.Trim();

            if (line.Length == 0) continue;

            int space = line.IndexOf(' ');

            if (space <= 0) continue;

            string hash = line[..space];
            string name = line[(space + 1)..].TrimStart('*').Trim();

            if (string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase)) return hash.ToLowerInvariant();
        }

        return null;
    }

    /// <summary>
    /// 从本地 zip 里取出一个条目到内存。先按完整路径找，找不到再按文件名找
    /// （不同打包工具的目录层级不一样）；都没有返回 null。
    /// </summary>
    public static byte[]? Extract(string zipPath, string entryName)
    {
        using ZipArchive archive = ZipFile.OpenRead(zipPath);

        ZipArchiveEntry? entry = archive.Entries.FirstOrDefault(
                item => string.Equals(item.FullName, entryName, StringComparison.OrdinalIgnoreCase))
            ?? archive.Entries.FirstOrDefault(
                item => string.Equals(item.Name, entryName, StringComparison.OrdinalIgnoreCase));

        if (entry is null) return null;

        using Stream stream = entry.Open();
        using MemoryStream memory = new((int)entry.Length);

        stream.CopyTo(memory);

        return memory.ToArray();
    }

    /// <summary>把一段字节写成文件：先写 .new 再改名，中途失败不会留下半截文件。</summary>
    /// <param name="path">目标文件。</param>
    /// <param name="data">内容。</param>
    /// <param name="backup">原文件的备份路径；传 null 表示不备份。</param>
    /// <returns>成功时返回 null，失败返回给人看的原因。</returns>
    public static string? ReplaceFile(string path, byte[] data, string? backup)
    {
        string temp = path + ".new";

        try
        {
            File.WriteAllBytes(temp, data);

            if (File.Exists(path))
            {
                if (backup is null) File.Delete(path);
                else File.Replace(temp, path, backup, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temp, path);
            }

            return null;
        }
        catch (Exception ex)
        {
            TryDelete(temp);

            return ex.Message;
        }
    }

    /// <summary>删文件，删不掉就算了（比如正被别的进程占着）。</summary>
    public static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception)
        {
            // 删不掉不影响主流程
        }
    }
}
