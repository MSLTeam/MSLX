using System.Collections.Concurrent;

namespace MSLX.Daemon.Services.InstanceServices;

/// <summary>
/// 实例运行时状态的存储：活跃实例、重启标记、客户端首选终端尺寸。
/// 注册为 DI 单例，供各实例服务共享。线程安全。
/// </summary>
public class InstanceStateStore
{
    private readonly ConcurrentDictionary<uint, ServerContext> _activeServers = new(); // 存储运行中实例的状态数据
    private readonly ConcurrentDictionary<uint, bool> _restartingServers = new(); // 存储正在重启的实例ID
    private readonly ConcurrentDictionary<uint, (int cols, int rows)> _preferredTerminalSizes = new(); // 记录客户端首选终端尺寸

    #region 活跃实例

    /// <summary>获取指定实例的运行时状态，不存在时返回 null</summary>
    public ServerContext? Get(uint instanceId)
        => _activeServers.TryGetValue(instanceId, out var context) ? context : null;

    /// <summary>写入（或覆盖）指定实例的运行时状态</summary>
    public void Set(uint instanceId, ServerContext context)
        => _activeServers[instanceId] = context;

    /// <summary>移除指定实例的运行时状态</summary>
    public void Remove(uint instanceId)
        => _activeServers.TryRemove(instanceId, out _);

    /// <summary>是否存在任何活跃实例</summary>
    public bool HasAnyActive => !_activeServers.IsEmpty;

    /// <summary>枚举当前活跃实例（遍历并发字典是线程安全的）</summary>
    public IEnumerable<KeyValuePair<uint, ServerContext>> ActiveEntries => _activeServers;

    /// <summary>清空全部活跃实例状态</summary>
    public void ClearAll() => _activeServers.Clear();

    #endregion

    #region 重启标记

    /// <summary>指定实例是否处于重启流程中</summary>
    public bool IsRestarting(uint instanceId)
        => _restartingServers.ContainsKey(instanceId);

    /// <summary>标记实例进入重启流程</summary>
    public void MarkRestarting(uint instanceId)
        => _restartingServers.TryAdd(instanceId, true);

    /// <summary>移除实例的重启标记</summary>
    public void ClearRestarting(uint instanceId)
        => _restartingServers.TryRemove(instanceId, out _);

    #endregion

    #region 客户端首选终端尺寸

    /// <summary>记录客户端首选的 PTY 终端行列尺寸</summary>
    public void SetPreferredTerminalSize(uint instanceId, int cols, int rows)
        => _preferredTerminalSizes[instanceId] = (cols, rows);

    /// <summary>尝试获取客户端首选的终端尺寸</summary>
    public bool TryGetPreferredTerminalSize(uint instanceId, out (int cols, int rows) size)
        => _preferredTerminalSizes.TryGetValue(instanceId, out size);

    #endregion
}
