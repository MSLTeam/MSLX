using Microsoft.AspNetCore.SignalR;
using MSLX.Daemon.Hubs;
using MSLX.Daemon.Utils;

namespace MSLX.Daemon.Services;

public class SystemMonitorWorker : BackgroundService
{
    private readonly SystemMonitor _monitor;
    private readonly IHubContext<SystemMonitorHub> _hubContext;
    private readonly ILogger<SystemMonitorWorker> _logger;

    // 推送间隔：2秒
    private const int UpdateIntervalMs = 2000;

    public SystemMonitorWorker(
        SystemMonitor monitor,
        IHubContext<SystemMonitorHub> hubContext,
        ILogger<SystemMonitorWorker> logger)
    {
        _monitor = monitor;
        _hubContext = hubContext;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[SystemMonitor] 后台监控服务已启动");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 获取数据
                var (cpu, totalMem, usedMem) = await _monitor.GetStatusAsync();

                // 构造数据包
                var payload = new
                {
                    timestamp = DateTime.Now.ToString("HH:mm:ss"), // 方便前端做X轴
                    cpu = cpu,           // CPU百分比 (0-100)
                    memTotal = totalMem, // 总内存 MB
                    memUsed = usedMem,   // 已用内存 MB
                    memUsage = totalMem > 0 ? Math.Round((usedMem / totalMem) * 100, 1) : 0 // 内存百分比
                };

                // 推送到 SignalR 组
                await _hubContext.Clients.Group(SystemMonitorHub.GroupName)
                    .SendAsync("ReceiveSystemStats", payload, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SystemMonitor] 采集或推送数据时出错");
            }

            // 等待下一轮
            await Task.Delay(UpdateIntervalMs, stoppingToken);
        }
    }
}