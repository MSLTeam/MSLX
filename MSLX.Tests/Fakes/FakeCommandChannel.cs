using MSLX.SDK.IServices;

namespace MSLX.Tests.Fakes;

/// <summary>记录全部命令发送、可控制运行状态的假命令通道</summary>
public class FakeCommandChannel : IInstanceLifecycleService, IInstanceConsoleService
{
    public bool Running { get; set; } = true;

    /// <summary>按发送顺序记录的所有命令</summary>
    public List<string> Commands { get; } = new();

    public bool IsServerRunning(uint instanceId) => Running;

    public bool SendCommand(uint instanceId, string command, bool repeatCommandToLog = false)
    {
        Commands.Add(command);
        return true;
    }

    // --- 未使用的存根方法 ---
    public (int status, string description) GetServerStatus(uint instanceId) => (0, "");
    public bool HasRunningServers() => false;
    public List<string> GetOnlinePlayers(uint instanceId) => new();
    public (bool success, string message) StartServer(uint instanceId, bool isAutoRestart = false, bool skipEulaCheck = false) => (false, "");
    public Task<bool> AgreeEULA(uint instanseId, bool agree) => Task.FromResult(false);
    public bool StopServer(uint instanceId) => false;
    public bool ForceKillServer(uint instanceId) => false;
    public Task<(bool success, string message)> RestartServer(uint instanceId) => Task.FromResult((false, ""));
    public List<string> GetLogs(uint instanceId) => new();
    public void StopAllServers() { }
    public TimeSpan GetServerUptime(uint instanceId) => TimeSpan.Zero;
    public bool StartBackupServer(uint instanceId) => false;
    public bool SendPtyInput(uint instanceId, byte[] data) => false;
    public bool SendPtyInput(uint instanceId, string data) => false;
    public bool ResizePty(uint instanceId, int cols, int rows) => false;
    public bool IsServerPtyMode(uint instanceId) => false;
    public List<string> GetPtyHistory(uint instanceId) => new();
}
