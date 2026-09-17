using MSLX.SDK.Models;

namespace MSLX.SDK.Events;

/// <summary>
/// 服务器准备启动事件参数
/// </summary>
public class ServerStartingEventArgs : EventArgs
{
    public uint InstanceId { get; set; }
    public McServerInfo.ServerInfo? ServerInfo { get; set; }

    /// <summary>
    /// 是否由自动重启触发
    /// </summary>
    public bool IsAutoRestart { get; set; }

    /// <summary>
    /// 是否取消启动
    /// </summary>
    public bool Cancel { get; set; } = false;

    /// <summary>
    /// 取消原因
    /// </summary>
    public string? CancelReason { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.Now;
}

/// <summary>
/// 服务器启动就绪/进程已启动事件参数
/// </summary>
public class ServerStartedEventArgs : EventArgs
{
    public uint InstanceId { get; set; }
    public McServerInfo.ServerInfo? ServerInfo { get; set; }
    public int ProcessId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

/// <summary>
/// 服务器正在停止事件参数
/// </summary>
public class ServerStoppingEventArgs : EventArgs
{
    public uint InstanceId { get; set; }
    public McServerInfo.ServerInfo? ServerInfo { get; set; }
    public string StopCommand { get; set; } = "stop";
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

/// <summary>
/// 服务器已完全停止退出事件参数
/// </summary>
public class ServerStoppedEventArgs : EventArgs
{
    public uint InstanceId { get; set; }
    public McServerInfo.ServerInfo? ServerInfo { get; set; }
    public int ExitCode { get; set; }
    public TimeSpan Uptime { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

/// <summary>
/// 服务器崩溃异常退出事件参数
/// </summary>
public class ServerCrashedEventArgs : EventArgs
{
    public uint InstanceId { get; set; }
    public McServerInfo.ServerInfo? ServerInfo { get; set; }
    public int ExitCode { get; set; }
    public string? CrashMessage { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

/// <summary>
/// 控制台日志接收事件参数
/// </summary>
public class ServerLogEventArgs : EventArgs
{
    public uint InstanceId { get; set; }
    public string LogLine { get; set; } = string.Empty;
    public bool IsStdErr { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

/// <summary>
/// 控制台命令执行前事件参数
/// </summary>
public class ServerCommandExecutingEventArgs : EventArgs
{
    public uint InstanceId { get; set; }
    public string Command { get; set; } = string.Empty;
    public bool SentViaRcon { get; set; }
    public bool Cancel { get; set; } = false;
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
