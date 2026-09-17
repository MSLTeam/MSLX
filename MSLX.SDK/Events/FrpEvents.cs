using Newtonsoft.Json.Linq;

namespace MSLX.SDK.Events;

/// <summary>
/// FRP 隧道准备启动事件参数
/// </summary>
public class FrpStartingEventArgs : EventArgs
{
    public int TunnelId { get; set; }
    public string TunnelName { get; set; } = string.Empty;
    public string? Service { get; set; }
    public string? ConfigType { get; set; }
    public JObject? Config { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

/// <summary>
/// FRP 隧道启动就绪/进程已启动事件参数
/// </summary>
public class FrpStartedEventArgs : EventArgs
{
    public int TunnelId { get; set; }
    public string TunnelName { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

/// <summary>
/// FRP 隧道正在停止事件参数
/// </summary>
public class FrpStoppingEventArgs : EventArgs
{
    public int TunnelId { get; set; }
    public string TunnelName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

/// <summary>
/// FRP 隧道已完全停止退出事件参数
/// </summary>
public class FrpStoppedEventArgs : EventArgs
{
    public int TunnelId { get; set; }
    public string TunnelName { get; set; } = string.Empty;
    public int ExitCode { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

/// <summary>
/// FRP 隧道控制台日志接收事件参数
/// </summary>
public class FrpLogEventArgs : EventArgs
{
    public int TunnelId { get; set; }
    public string LogLine { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
