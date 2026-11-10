using System.Reflection;

namespace MediaMTX_Yuki_GUI.Services;

// ====== 单文件解压缓存清理 ======
//
// 发的是单文件（PublishSingleFile + IncludeNativeLibrariesForSelfExtract），
// native 库（libSkiaSharp / libHarfBuzzSharp）不在 exe 旁边，进程跑起来之前
// apphost 会把它们解压到
//     %TEMP%\.net\<应用名>\<内容哈希>\
// 哈希由 bundle 内容算出，所以每换一版 exe 就多留一份，旧的永远不会被删，
// 每份约 12.8 MB，装几次就白占几十 MB。
//
// 这里在启动早期扫一遍同一层，把不是当前进程在用的目录删掉。
// 判据不用猜哈希：当前进程正加载的那份在删除时必然失败（文件被映射着），
// 删不掉就跳过——这一趟天然是安全的，不会把自己的库删掉。

public static class BundleCacheCleaner
{

    // ====== 对外入口 ======

    /// <summary>
    /// 后台清理旧的解压缓存。放在窗口起来之后调，别挡启动。
    /// </summary>
    public static void CleanInBackground()
    {
        string? root = ResolveCacheRoot();
        if (root is null) return;                                   // 不是单文件运行，没有这层目录

        _ = Task.Run(() => Clean(root));
    }


    // ====== 内部实现 ======

    private static string? ResolveCacheRoot()
    {
        string appName = Assembly.GetEntryAssembly()?.GetName().Name ?? string.Empty;
        if (appName.Length == 0) return null;

        string temp = Path.GetTempPath();
        if (temp.Length == 0) return null;

        string root = Path.Combine(temp, ".net", appName);
        return Directory.Exists(root) ? root : null;
    }

    private static void Clean(string root)
    {
        string? current = ResolveCurrentDirectory();
        if (current is null) return;

        string currentFull = EnsureTrailingSeparator(Path.GetFullPath(current));

        string[] candidates;
        try
        {
            candidates = Directory.GetDirectories(root);
        }
        catch
        {
            return;                                                 // 权限或并发问题，放弃这一趟
        }

        foreach (string dir in candidates)
        {
            if (EnsureTrailingSeparator(Path.GetFullPath(dir)) == currentFull) continue;
            TryDelete(dir);
        }
    }

    /// <summary>
    /// 当前进程正在用的那个解压目录。宿主机把 native 库搜索目录报在
    /// NATIVE_DLL_SEARCH_DIRECTORIES 里，取落在 .net&lt;分隔符&gt; 下的那一个。
    /// </summary>
    private static string? ResolveCurrentDirectory()
    {
        if (AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") is not string dirs) return null;

        string marker = Path.DirectorySeparatorChar + ".net" + Path.DirectorySeparatorChar;

        foreach (string raw in dirs.Split(Path.PathSeparator))
        {
            string dir = raw.Trim().Trim('"');
            if (dir.Length == 0) continue;
            if (dir.Contains(marker, StringComparison.OrdinalIgnoreCase)) return dir;
        }

        return null;
    }

    private static void TryDelete(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // 文件还被映射着（正在运行的另一个版本），或者被别的进程占着：留着，下次再说
        }
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
    }

}
