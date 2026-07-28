using System.Collections.Concurrent;

namespace MSLX.Daemon.Utils.BackgroundTasks;

/// <summary>
/// 跟踪正在执行的创建任务，支持按 serverId 取消
/// </summary>
public class CreationTaskTracker
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeTasks = new();

    /// <summary>
    /// 注册一个正在执行的任务
    /// </summary>
    public CancellationTokenSource Register(string serverId)
    {
        var cts = new CancellationTokenSource();
        _activeTasks[serverId] = cts;
        return cts;
    }

    /// <summary>
    /// 移除已完成/已取消的任务
    /// </summary>
    public void Unregister(string serverId)
    {
        _activeTasks.TryRemove(serverId, out _);
    }

    /// <summary>
    /// 尝试取消指定任务
    /// </summary>
    public bool TryCancel(string serverId)
    {
        if (_activeTasks.TryGetValue(serverId, out var cts))
        {
            cts.Cancel();
            return true;
        }
        return false;
    }
}
