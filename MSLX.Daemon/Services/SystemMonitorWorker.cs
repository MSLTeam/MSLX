using Microsoft.AspNetCore.SignalR;
using MSLX.Daemon.Hubs;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.Models.Node;
using System.Text.Json;
using System.Text;

namespace MSLX.Daemon.Services;

public class SystemMonitorWorker : BackgroundService
{
    private readonly SystemMonitor _monitor;
    private readonly IHubContext<SystemMonitorHub> _hubContext;
    private readonly ILogger<SystemMonitorWorker> _logger;

    // 推送间隔：2秒
    private const int UpdateIntervalMs = 2000;
    
    private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool?> _masterConnStatus = new();

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

                    var payload = new NodeStatsPayload
                    {
                        timestamp = DateTime.Now.ToString("HH:mm:ss"),
                        cpu = cpu,
                        memTotal = totalMem,
                        memUsed = usedMem,
                        memUsage = totalMem > 0 ? Math.Round((usedMem / totalMem) * 100, 1) : 0
                    };

                    bool isSlaveMode = bool.Parse(IConfigBase.Config.ReadConfigKey("IsSlaveMode")?.ToString() ?? "false");

                    if (isSlaveMode)
                    {
                        // 发送给所有链接的主节点
                        var masters = IConfigBase.MasterNodes.GetAllMasters();
                        foreach (var master in masters)
                        {
                            try
                            {
                                var req = new HttpRequestMessage(HttpMethod.Post, $"{master.MasterUrl.TrimEnd('/')}/api/node/report-stats");
                                req.Headers.Add("x-api-key", master.CommsKey);
                                req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                                
                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                        var res = await _httpClient.SendAsync(req, stoppingToken);
                                        _masterConnStatus.TryGetValue(master.MasterId, out bool? lastStatus);
                                        if (res.IsSuccessStatusCode)
                                        {
                                            if (lastStatus != true)
                                            {
                                                _logger.LogInformation("与主节点建立连接成功，已开始同步上报性能数据: {MasterUrl}", master.MasterUrl);
                                                _masterConnStatus[master.MasterId] = true;
                                            }
                                        }
                                        else
                                        {
                                            if (lastStatus != false)
                                            {
                                                _logger.LogWarning("向主节点上报性能数据失败，状态码: {StatusCode}, 主节点地址: {MasterUrl}", res.StatusCode, master.MasterUrl);
                                                _masterConnStatus[master.MasterId] = false;
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _masterConnStatus.TryGetValue(master.MasterId, out bool? lastStatus);
                                        if (lastStatus != false)
                                        {
                                            _logger.LogWarning("无法连接到主节点进行性能上报 {MasterUrl}: {Message}", master.MasterUrl, ex.Message);
                                            _masterConnStatus[master.MasterId] = false;
                                        }
                                    }
                                }, stoppingToken);
                            }
                            catch { }
                        }
                    }
                    
                    // 过滤掉超过 10 秒未上报的子节点数据
                    var activeSlaves = NodeStatsManager.SlaveStats
                        .Where(kv => (DateTime.Now - kv.Value.LastUpdated).TotalSeconds < 10)
                        .ToDictionary(kv => kv.Key, kv => kv.Value.Stats);

                    // 无论主从，都可以本地广播系统状态。如果是主节点，合并子节点状态广播。
                    var broadcastPayload = new
                    {
                        local = payload,
                        slaves = activeSlaves
                    };

                    await _hubContext.Clients.Group(SystemMonitorHub.GroupName)
                        .SendAsync("ReceiveSystemStats", broadcastPayload, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
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