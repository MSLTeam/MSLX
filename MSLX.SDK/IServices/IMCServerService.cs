namespace MSLX.SDK.Services;

public interface IMCServerService
{
    /// <summary>
    /// 检查服务器是否正在运行
    /// </summary>
    bool IsServerRunning(uint instanceId);

    /// <summary>
    /// 获取服务器详细状态
    /// 0:未启动, 1:启动中, 2:运行中, 3:停止中, 4:重启中
    /// </summary>
    (int status, string description) GetServerStatus(uint instanceId);

    /// <summary>
    /// 检查是否有任何服务器实例处于活动状态
    /// </summary>
    bool HasRunningServers();

    /// <summary>
    /// 获取指定实例当前的在线玩家列表
    /// </summary>
    List<string> GetOnlinePlayers(uint instanceId);

    /// <summary>
    /// 启动 MC 服务器 (非阻塞模式)
    /// </summary>
    (bool success, string message) StartServer(uint instanceId,
        bool isAutoRestart = false, bool skipEulaCheck = false);

    Task<bool> AgreeEULA(uint instanseId, bool agree);

    /// <summary>
    /// 停止服务器
    /// </summary>
    bool StopServer(uint instanceId);

    /// <summary>
    /// 强制终止服务器进程
    /// </summary>
    bool ForceKillServer(uint instanceId);

    /// <summary>
    /// 重启服务器 (停止 -> 等待 -> 启动)
    /// </summary>
    Task<(bool success, string message)> RestartServer(uint instanceId);

    /// <summary>
    /// 向服务器发送命令
    /// </summary>
    bool SendCommand(uint instanceId, string command, bool repeatCommandToLog = false);

    /// <summary>
    /// 获取服务器日志
    /// </summary>
    List<string> GetLogs(uint instanceId);

    /// <summary>
    /// 停止所有服务器
    /// </summary>
    void StopAllServers();

    /// <summary>
    /// 获取服务器已运行的时间
    /// </summary>
    TimeSpan GetServerUptime(uint instanceId);

    bool StartBackupServer(uint instanceId);
}