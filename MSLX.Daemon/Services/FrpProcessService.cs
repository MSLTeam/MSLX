using System.Collections.Concurrent;
using System.Diagnostics;

namespace MSLX.Daemon.Services;

public class FrpProcessService
{
    // 用字典存储进程，键为ID，值为进程对象
    private readonly ConcurrentDictionary<int, Process> _activeProcesses = new();
    private readonly ILogger<FrpProcessService> _logger;

    public FrpProcessService(ILogger<FrpProcessService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 检查指定ID的FRP隧道是否正在运行
    /// </summary>
    public bool IsFrpRunning(int id)
    {
        if (_activeProcesses.TryGetValue(id, out var process))
        {
            if (process != null && !process.HasExited)
            {
                return true;
            }
            
            // 进程挂了就别在字典呆着了～
            _activeProcesses.TryRemove(id, out _);
        }
        return false;
    }

    // TODO: 后续在这里添加 StartFrp, StopFrp 和 获取日志的方法
}