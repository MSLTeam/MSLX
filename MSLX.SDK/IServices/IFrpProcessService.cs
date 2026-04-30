namespace MSLX.SDK.IServices;

public interface IFrpProcessService
{
    bool IsFrpRunning(int id);

    /// <summary>
    /// 启动 FRP 进程 (非阻塞模式)
    /// </summary>
    (bool success, string message) StartFrp(int id);

    bool StopFrp(int id);
    List<string> GetLogs(int id);
}