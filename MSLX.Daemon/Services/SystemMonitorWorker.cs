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
    private readonly NodeRemovalRetries _removalRetries = new();

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
                        // Independent relationships run together, with no overlapping report per tick.
                        await Task.WhenAll(IConfigBase.MasterNodes.GetAllMasters().Select(async master =>
                        {
                            try
                            {
                                using var req = new HttpRequestMessage(HttpMethod.Post, $"{master.MasterUrl.TrimEnd('/')}/api/node/report-stats");
                                req.Headers.Add("x-api-key", master.CommsKey);
                                req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                                using var response = await _httpClient.SendAsync(req, stoppingToken);
                                if (response.StatusCode == System.Net.HttpStatusCode.Gone)
                                    IConfigBase.MasterNodes.RemoveRelationship(master.NodeId, master.CommsKey);
                                _masterConnStatus.TryGetValue(master.MasterId, out var previous);
                                if (previous != response.IsSuccessStatusCode)
                                    _logger.LogInformation("主节点上报状态: {MasterUrl}, HTTP {Status}", master.MasterUrl, (int)response.StatusCode);
                                _masterConnStatus[master.MasterId] = response.IsSuccessStatusCode;
                            }
                            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                            {
                                if (!_masterConnStatus.TryGetValue(master.MasterId, out var previous) || previous != false)
                                    _logger.LogWarning("主节点暂时不可用: {MasterUrl}, {Message}", master.MasterUrl, ex.Message);
                                _masterConnStatus[master.MasterId] = false;
                            }
                        }));
                    }
                    var pendingNodes = IConfigBase.NodeList.GetAllNodes().Where(n => n.PendingRemoval).ToList();
                    _removalRetries.Retain(pendingNodes.Select(n => n.NodeId));
                    await Task.WhenAll(pendingNodes.Where(n => _removalRetries.IsDue(n.NodeId, DateTimeOffset.UtcNow)).Select(async node =>
                    {
                        try
                        {
                            using var req = new HttpRequestMessage(HttpMethod.Post, $"{node.NodeUrl.TrimEnd('/')}/api/node/remove-link");
                            req.Content = System.Net.Http.Json.JsonContent.Create(new { node.NodeId, node.CommsKey, MasterUrl = node.MasterUrl });
                            using var response = await _httpClient.SendAsync(req, stoppingToken);
                            if (response.IsSuccessStatusCode)
                            {
                                IConfigBase.NodeList.RemovePendingNode(node.NodeId, node.CommsKey);
                                return;
                            }
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested) { }
                        if (_removalRetries.Failed(node.NodeId, DateTimeOffset.UtcNow) == 1)
                            _logger.LogWarning("子节点清理暂未完成，将逐步降低重试频率: {NodeId}", node.NodeId);
                    }));

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

public sealed class NodeRemovalRetries
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (int Attempts, DateTimeOffset Next)> _entries = new();
    private static readonly int[] Delays = [5, 15, 30, 60, 300];
    public bool IsDue(string nodeId, DateTimeOffset now) => !_entries.TryGetValue(nodeId, out var entry) || entry.Next <= now;
    public int Failed(string nodeId, DateTimeOffset now)
    {
        var entry = _entries.AddOrUpdate(nodeId, _ => (1, now.AddSeconds(Delays[0])), (_, previous) =>
        {
            var attempts = Math.Min(previous.Attempts + 1, Delays.Length);
            return (attempts, now.AddSeconds(Delays[attempts - 1]));
        });
        return entry.Attempts;
    }
    public void Retain(IEnumerable<string> ids)
    {
        var active = ids.ToHashSet();
        foreach (var id in _entries.Keys) if (!active.Contains(id)) _entries.TryRemove(id, out _);
    }
}
