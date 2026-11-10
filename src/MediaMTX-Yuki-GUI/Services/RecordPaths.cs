using System;
using System.Collections.Generic;
using System.IO;

namespace MediaMTX_Yuki_GUI.Services;

/// <summary>
/// 录制目录模板的解析。模板形如 <c>./recordings/%path/%Y-%m-%d_%H-%M-%S-%f_%z</c>：
/// 前面是不带变量的目录段，<c>%path</c> 展开成源名，后面全是文件名里的时间变量。
/// </summary>
/// <remarks>
/// 相对路径按<b>本程序目录</b>解析，不按引擎的工作目录——
/// 引擎默认 cwd 是 MediaMTX 子目录，照它的 cwd 算会录到 <c>MediaMTX\recordings</c> 里去，
/// 用户找不到。所以写进 runtime.yml 时要把前缀换成绝对路径，界面显示的也用同一套算法。
/// </remarks>
public static class RecordPaths
{
    /// <summary>模板里 <c>%path</c> 展开成什么；不传就是只算目录根。</summary>
    private const string PathVariable = "%path";

    /// <summary>
    /// 录制目录的根：模板里第一个带变量的段之前那段。
    /// 模板没写或者一上来就是变量时退回程序目录。
    /// </summary>
    public static string Root(string template)
        => Combine(template, null);

    /// <summary>某条 path 的录制目录。</summary>
    public static string ForPath(string template, string path)
        => Combine(template, path);

    /// <summary>
    /// 写进 runtime.yml 的模板：把前缀换成绝对路径，变量原样留在后面。
    /// 引擎自己不认识相对路径是相对谁，只有绝对路径才落得准。
    /// </summary>
    public static string Absolute(string template)
    {
        List<string> head = Head(template);

        if (head.Count == 0) return template.Trim();

        string prefix = Resolve(head);
        string rest = Tail(template);

        return rest.Length == 0 ? prefix : prefix + "/" + rest;
    }

    private static string Combine(string template, string? path)
    {
        List<string> head = Head(template);

        if (path is null) return head.Count == 0 ? AppPaths.Root : Resolve(head);

        head.Add(path);

        return Resolve(head);
    }

    /// <summary>模板里第一个带 <c>%</c> 的段之前的那些目录段。</summary>
    private static List<string> Head(string template)
    {
        List<string> segments = [];

        foreach (string part in Split(template))
        {
            if (part.Contains('%', StringComparison.Ordinal)) break;

            segments.Add(part);
        }

        return segments;
    }

    /// <summary>第一个带 <c>%</c> 的段开始往后的原始文本（不含它前面的分隔符）。</summary>
    private static string Tail(string template)
    {
        string text = template.Trim().Replace('\\', '/');
        int cut = -1;

        foreach (string part in Split(text))
        {
            if (!part.Contains('%', StringComparison.Ordinal)) continue;

            cut = text.IndexOf(part, StringComparison.Ordinal);
            break;
        }

        return cut < 0 ? string.Empty : text[cut..];
    }

    private static string[] Split(string template)
        => template.Trim().Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>相对路径挂在程序目录下，绝对路径原样用。</summary>
    private static string Resolve(List<string> segments)
    {
        string combined = Path.Combine([.. segments]);

        return Path.GetFullPath(Path.IsPathRooted(combined)
            ? combined
            : Path.Combine(AppPaths.Root, combined));
    }
}