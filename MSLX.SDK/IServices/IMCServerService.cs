

namespace MSLX.SDK.IServices;

public interface IMCServerService
{
    bool IsServerRunning(uint instanceId);
    (int status, string description) GetServerStatus(uint instanceId);
    bool HasRunningServers();
    List<string> GetOnlinePlayers(uint instanceId);

    (bool success, string message) StartServer(uint instanceId,
        bool isAutoRestart = false, bool skipEulaCheck = false);

    Task<bool> AgreeEULA(uint instanseId, bool agree);
    bool StopServer(uint instanceId);
    bool ForceKillServer(uint instanceId);
    Task<(bool success, string message)> RestartServer(uint instanceId);
    bool SendCommand(uint instanceId, string command, bool repeatCommandToLog = false);
    List<string> GetLogs(uint instanceId);
    void StopAllServers();
    TimeSpan GetServerUptime(uint instanceId);
    bool StartBackupServer(uint instanceId);
}