using System.Collections.Concurrent;

namespace MSLX.Daemon.Utils.BackgroundTasks;

/// <summary>
/// 跟踪正在执行的创建任务，支持按 serverId 取消
/// </summary>
public class CreationTaskTracker
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activeTasks = new();
    private readonly ConcurrentDictionary<string, bool> _cleanupPreferences = new();

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
        _cleanupPreferences.TryRemove(serverId, out _);
    }

    /// <summary>
    /// 尝试取消指定任务
    /// </summary>
    /// <param name="serverId">服务器ID</param>
    /// <param name="cleanupFiles">是否清理文件</param>
    public bool TryCancel(string serverId, bool cleanupFiles = false)
    {
        if (_activeTasks.TryGetValue(serverId, out var cts))
        {
            _cleanupPreferences[serverId] = cleanupFiles;
            cts.Cancel();
            return true;
        }
        return false;
    }
    
    /// <summary>
    /// 获取取消时是否需要清理文件
    /// </summary>
    public bool ShouldCleanupFiles(string serverId)
    {
        return _cleanupPreferences.TryGetValue(serverId, out var cleanup) && cleanup;
    }
}
