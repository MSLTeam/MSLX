namespace MSLX.SDK.IServices;

/// <summary>
/// 实例生命周期与状态服务
/// </summary>
public interface IInstanceLifecycleService
{
    /// <summary>
    /// 检查实例是否正在运行
    /// </summary>
    bool IsServerRunning(uint instanceId);

    /// <summary>
    /// 获取实例详细状态
    /// 0:未启动, 1:启动中, 2:运行中, 3:停止中, 4:重启中
    /// </summary>
    (int status, string description) GetServerStatus(uint instanceId);

    /// <summary>
    /// 检查是否有任何实例处于活动状态
    /// </summary>
    bool HasRunningServers();

    /// <summary>
    /// 获取指定实例当前的在线玩家列表
    /// </summary>
    List<string> GetOnlinePlayers(uint instanceId);

    /// <summary>
    /// 启动实例 (非阻塞模式)
    /// </summary>
    (bool success, string message) StartServer(uint instanceId, bool isAutoRestart = false, bool skipEulaCheck = false);

    /// <summary>
    /// 同意最终用户许可协议
    /// </summary>
    Task<bool> AgreeEULA(uint instanceId, bool agree);

    /// <summary>
    /// 优雅停止实例
    /// </summary>
    bool StopServer(uint instanceId);

    /// <summary>
    /// 强制终止实例进程
    /// </summary>
    bool ForceKillServer(uint instanceId);

    /// <summary>
    /// 重启实例 (停止 -> 等待 -> 启动)
    /// </summary>
    Task<(bool success, string message)> RestartServer(uint instanceId);

    /// <summary>
    /// 停止所有实例
    /// </summary>
    void StopAllServers();

    /// <summary>
    /// 获取实例已连续运行的时间
    /// </summary>
    TimeSpan GetServerUptime(uint instanceId);
}
