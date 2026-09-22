using MSLX.SDK.IServices;

namespace MSLX.Daemon.Services.InstanceServices;

/// <summary>
/// 兼容旧版 IMCServerService 的适配器，将调用转发给新的细分接口
/// </summary>
#pragma warning disable CS0618
public class LegacyMCServerServiceAdapter : IMCServerService
{
    private readonly IInstanceLifecycleService _lifecycle;
    private readonly IInstanceConsoleService _console;
    private readonly IInstanceBackupService _backup;

    public LegacyMCServerServiceAdapter(
        IInstanceLifecycleService lifecycle,
        IInstanceConsoleService console,
        IInstanceBackupService backup)
    {
        _lifecycle = lifecycle;
        _console = console;
        _backup = backup;
    }

    public bool IsServerRunning(uint instanceId) => _lifecycle.IsServerRunning(instanceId);
    public (int status, string description) GetServerStatus(uint instanceId) => _lifecycle.GetServerStatus(instanceId);
    public bool HasRunningServers() => _lifecycle.HasRunningServers();
    public List<string> GetOnlinePlayers(uint instanceId) => _lifecycle.GetOnlinePlayers(instanceId);
    public (bool success, string message) StartServer(uint instanceId, bool isAutoRestart = false, bool skipEulaCheck = false) => _lifecycle.StartServer(instanceId, isAutoRestart, skipEulaCheck);
    public Task<bool> AgreeEULA(uint instanseId, bool agree) => _lifecycle.AgreeEULA(instanseId, agree);
    public bool StopServer(uint instanceId) => _lifecycle.StopServer(instanceId);
    public bool ForceKillServer(uint instanceId) => _lifecycle.ForceKillServer(instanceId);
    public Task<(bool success, string message)> RestartServer(uint instanceId) => _lifecycle.RestartServer(instanceId);
    public void StopAllServers() => _lifecycle.StopAllServers();
    public TimeSpan GetServerUptime(uint instanceId) => _lifecycle.GetServerUptime(instanceId);
    
    public bool SendCommand(uint instanceId, string command, bool repeatCommandToLog = false) => _console.SendCommand(instanceId, command, repeatCommandToLog);
    public bool SendPtyInput(uint instanceId, byte[] data) => _console.SendPtyInput(instanceId, data);
    public bool SendPtyInput(uint instanceId, string data) => _console.SendPtyInput(instanceId, data);
    public bool ResizePty(uint instanceId, int cols, int rows) => _console.ResizePty(instanceId, cols, rows);
    public bool IsServerPtyMode(uint instanceId) => _console.IsServerPtyMode(instanceId);
    public List<string> GetLogs(uint instanceId) => _console.GetLogs(instanceId);
    public List<string> GetPtyHistory(uint instanceId) => _console.GetPtyHistory(instanceId);

    public bool StartBackupServer(uint instanceId) => _backup.StartBackupServer(instanceId);
}
#pragma warning restore CS0618
