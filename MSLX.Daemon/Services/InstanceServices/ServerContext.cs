using Porta.Pty;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace MSLX.Daemon.Services.InstanceServices;

/// <summary>
/// 单个运行中实例的全部运行时状态（进程句柄、PTY、日志缓冲、玩家列表、退出标志等）。
/// 由 <see cref="InstanceStateStore"/> 统一管理。
/// </summary>
public class ServerContext
{
    public Process? Process { get; set; }
    public IPtyConnection? PtyConnection { get; set; }
    public bool IsPtyMode { get; set; } = false;
    public CancellationTokenSource? PtyReadCts { get; set; }
    public ConcurrentQueue<string> Logs { get; set; } = new();
    public ConcurrentQueue<string> PtyHistory { get; set; } = new();
    public bool IsInitializing { get; set; } = false;
    public volatile bool IsStopping = false;
    public volatile bool IsBackuping = false;
    public volatile bool MonitorPlayers = true;
    public ConcurrentDictionary<string, bool> OnlinePlayers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // 用于计算资源使用率
    public TimeSpan PreviousTotalProcessorTime { get; set; } = TimeSpan.Zero;
    public DateTime PreviousCpuCheckTime { get; set; } = DateTime.MinValue;

    public Process? MonitorProcess { get; set; } // win下监控的进程
    public int LastMonitoredPid { get; set; } = -1;

    // 用于子进程脱出的监控
    public object StateLock = new object();
    public volatile bool IsProcessExited = false;
    public volatile bool IsStdoutClosed = false;
    public volatile bool IsStderrClosed = false;
    public volatile int FinalExitCode = 0;
    public volatile bool HasTriggeredExit = false;

    // Docker监控相关
    public bool IsDocker { get; set; }
    public double CpuBaseLimitPercentage { get; set; } = 0;

    // 实例输入输出编码
    public Encoding InputEncoding { get; set; } = Encoding.UTF8;
    public Encoding OutputEncoding { get; set; } = Encoding.UTF8;
}
