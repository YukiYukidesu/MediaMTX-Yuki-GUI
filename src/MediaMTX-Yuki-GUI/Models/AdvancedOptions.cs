namespace MediaMTX_Yuki_GUI.Models;

/// <summary>容错与调参。默认值都取 MediaMTX 自己的默认，不擅自改动行为。</summary>
public sealed class AdvancedOptions
{
    /// <summary>同一路流最多几个读者，0 表示不限。</summary>
    public int MaxReaders { get; set; }

    /// <summary>没人推流时是否用占位流顶上，让播放端不用等。</summary>
    public bool AlwaysAvailable { get; set; }

    /// <summary>新的推流是否踢掉旧的推流。关掉的话，旧推流还在时新来的会被拒。</summary>
    public bool OverridePublisher { get; set; } = true;

    /// <summary>读超时，单位秒。</summary>
    public int ReadTimeoutSeconds { get; set; } = 10;

    /// <summary>写超时，单位秒。</summary>
    public int WriteTimeoutSeconds { get; set; } = 10;

    /// <summary>每个连接的写队列长度，调大能吸收突发，代价是内存。</summary>
    public int WriteQueueSize { get; set; } = 512;
}
