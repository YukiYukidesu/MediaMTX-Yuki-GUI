using Avalonia;
using Avalonia.Controls;

namespace MediaMTX_Yuki_GUI.Controls;

/// <summary>
/// 关系图所有节点的位移表：整张图只认这一份。
/// </summary>
/// <remarks>
/// 节点与连线原本各写一套缓动，靠「时间常数一样、起点一样」隐式同步——
/// 那是两套独立的动画帧，任何一边掉一帧就错开，线头会从胶囊边缘脱开。
/// 现在两边读同一张表：<see cref="RelationCanvas"/> 重新布局时把「旧位置 − 新位置」写进来，
/// 这里推进收敛，<see cref="RelationLinks"/> 渲染时直接查两端节点此刻的位移。
/// 线与点位置同源，不存在谁先到。
/// <para>
/// 做成单例是因为同一时刻界面上只会有一个关系图；页面被摘下时由画布调 <see cref="Clear"/> 收干净。
/// </para>
/// </remarks>
internal sealed class MapMotion
{
    /// <summary>整张图共用的一份位移。</summary>
    public static readonly MapMotion Shared = new();

    /// <summary>缓动速度：每秒朝目标逼近的比例，按 1 - e^(-Chase * dt) 走。</summary>
    private const double Chase = 18;

    /// <summary>离目标不到这么多像素就算到位，直接吸上去，省掉最后几帧的抖动。</summary>
    private const double Settle = 0.35;

    /// <summary>单帧最多按这么多秒算：窗口卡一下之后别让节点一下飞太猛。</summary>
    private const double MaxFrameSeconds = 0.05;

    /// <summary>节点键 → 此刻相对布局位置的平移补偿。</summary>
    private readonly Dictionary<string, Vector> _shifts = new(StringComparer.Ordinal);

    /// <summary>动画帧是否已经排上：同一时间只排一串，免得重复请求。</summary>
    private bool _running;

    /// <summary>上一帧的时间戳，用来算这一帧该走多少。</summary>
    private TimeSpan? _lastStamp;

    /// <summary>排帧用的顶层窗口，推进期间一直用它。</summary>
    private TopLevel? _host;

    /// <summary>推进了一帧：节点层与连线层都挂在这上面重画，保证两边看到的是同一份位移。</summary>
    public event Action? Moved;


    /// <summary>取某个节点此刻的位移补偿；没有就是零。</summary>
    public Vector Get(string key)
    {
        if (string.IsNullOrEmpty(key)) return default;

        return _shifts.TryGetValue(key, out Vector shift) ? shift : default;
    }

    /// <summary>写入某个节点的新位移；低于阈值就当作已到位，直接从表里摘掉。</summary>
    public void Set(string key, Vector shift)
    {
        if (string.IsNullOrEmpty(key)) return;

        if (Math.Abs(shift.X) < Settle && Math.Abs(shift.Y) < Settle)
        {
            _shifts.Remove(key);
            return;
        }

        _shifts[key] = shift;
    }

    /// <summary>排上动画帧；拿不到顶层窗口就什么都不做，下次布局还会再调。</summary>
    public void Start(TopLevel? host)
    {
        if (host is null) return;

        _host = host;

        if (_running || _shifts.Count == 0) return;

        _running = true;
        _lastStamp = null;
        host.RequestAnimationFrame(Advance);
    }

    /// <summary>把已经不在图里的节点从表里摘掉。</summary>
    public void Prune(HashSet<string> alive)
    {
        if (_shifts.Count == 0) return;

        List<string> dead = [];

        foreach (string key in _shifts.Keys)
        {
            if (!alive.Contains(key)) dead.Add(key);
        }

        foreach (string key in dead) _shifts.Remove(key);
    }

    /// <summary>全部清零并停帧：页面切走、或数据整体重置时用。</summary>
    public void Clear()
    {
        _shifts.Clear();
        _running = false;
        _lastStamp = null;
        _host = null;
    }


    /// <summary>一帧：每个补偿量朝 0 收一段，收干净就停。</summary>
    private void Advance(TimeSpan stamp)
    {
        double seconds = _lastStamp is { } last ? (stamp - last).TotalSeconds : 0;
        _lastStamp = stamp;

        if (seconds > MaxFrameSeconds) seconds = MaxFrameSeconds;

        if (seconds <= 0)
        {
            _host?.RequestAnimationFrame(Advance);
            return;
        }

        double follow = 1 - Math.Exp(-Chase * seconds);
        List<string> landed = [];

        foreach ((string key, Vector shift) in _shifts)
        {
            Vector next = shift - shift * follow;

            if (Math.Abs(next.X) >= Settle || Math.Abs(next.Y) >= Settle)
            {
                _shifts[key] = next;
                continue;
            }

            landed.Add(key);
        }

        foreach (string key in landed) _shifts.Remove(key);

        Moved?.Invoke();

        if (_shifts.Count == 0)
        {
            _running = false;
            _lastStamp = null;
            return;
        }

        _host?.RequestAnimationFrame(Advance);
    }
}
