namespace MediaMTX_Yuki_GUI.Models;

/// <summary>
/// 本地录制：引擎把收到的流原样写盘，不转码，CPU 几乎不涨。
/// 这一组对应引擎 pathDefaults 里的 record 系列键。
/// </summary>
public sealed class RecordOptions
{
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 录到哪个目录。可以用 %path / %Y / %m / %d / %H / %M / %S / %f / %z 这些变量，
    /// 默认按「路径名 / 日期」分文件夹，一个流一天一堆文件。
    /// </summary>
    public string PathTemplate { get; set; } = "./recordings/%path/%Y-%m-%d_%H-%M-%S-%f_%z";

    /// <summary>fmp4（边录边能放）或 mpegts（老播放器更认）。</summary>
    public string Format { get; set; } = "fmp4";

    /// <summary>一个分段多长，形如 1h / 30m / 10s。</summary>
    public string SegmentDuration { get; set; } = "1h";

    /// <summary>单个分段文件多大就切，形如 50M / 1G。</summary>
    public string MaxPartSize { get; set; } = "50M";

    /// <summary>旧分段留多久自动删，形如 1d / 7d；填 0 表示永不删。</summary>
    public string DeleteAfter { get; set; } = "1d";
}
