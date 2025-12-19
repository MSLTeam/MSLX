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

        // 创建定时器
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(UpdateIntervalMs));

        try
        {
            do
            {
                try
                {
                    var (cpu, totalMem, usedMem) = await _monitor.GetStatusAsync();

                    var payload = new
                    {
                        timestamp = DateTime.Now.ToString("HH:mm:ss"),
                        cpu = cpu,
                        memTotal = totalMem,
                        memUsed = usedMem,
                        memUsage = totalMem > 0 ? Math.Round((usedMem / totalMem) * 100, 1) : 0
                    };

                    await _hubContext.Clients.Group(SystemMonitorHub.GroupName)
                        .SendAsync("ReceiveSystemStats", payload, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // 捕获业务逻辑报错，但不捕获取消异常
                    _logger.LogError(ex, "[SystemMonitor] 采集或推送数据时出错");
                }

            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // 忽略取消异常
        }

        _logger.LogInformation("[SystemMonitor] 后台监控服务已停止");
    }
}