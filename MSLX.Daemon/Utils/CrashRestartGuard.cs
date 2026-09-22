using System.Collections.Concurrent;

namespace MSLX.Daemon.Utils;

/// <summary>
/// 崩溃重启熔断器：记录每个实例在滑动时间窗口内的崩溃次数，
/// 由调用方根据返回次数与 <see cref="MaxCount"/> 决定是否放弃自动重启。
/// 线程安全。
/// </summary>
public class CrashRestartGuard
{
    private readonly ConcurrentDictionary<uint, List<DateTime>> _history = new();
    private readonly TimeSpan _window;

    /// <summary>统计窗口长度（秒）</summary>
    public int WindowSeconds { get; }

    /// <summary>窗口内允许的最大崩溃次数，超过则应熔断</summary>
    public int MaxCount { get; }

    public CrashRestartGuard(int windowSeconds, int maxCount)
    {
        WindowSeconds = windowSeconds;
        MaxCount = maxCount;
        _window = TimeSpan.FromSeconds(windowSeconds);
    }

    /// <summary>
    /// 记录一次崩溃，返回当前时间窗口内的崩溃次数（含本次）。
    /// 返回值大于 <see cref="MaxCount"/> 时应放弃重启。
    /// </summary>
    public int RecordCrash(uint instanceId, DateTime now)
    {
        var history = _history.GetOrAdd(instanceId, _ => new List<DateTime>());

        // 加锁处理 List
        lock (history)
        {
            history.Add(now); // 记录本次崩溃时间

            // 清理超出时间窗口的旧记录
            history.RemoveAll(t => t < now - _window);

            return history.Count;
        }
    }

    /// <summary>
    /// 清除指定实例的崩溃记录
    /// </summary>
    public void Reset(uint instanceId)
    {
        _history.TryRemove(instanceId, out _);
    }
}
