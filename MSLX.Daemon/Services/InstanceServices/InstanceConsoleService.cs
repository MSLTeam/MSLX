using Microsoft.AspNetCore.SignalR;
using MSLX.Daemon.Hubs;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.Events;
using MSLX.SDK.Interfaces;

namespace MSLX.Daemon.Services.InstanceServices;

/// <summary>
/// 实例控制台服务：日志环形缓冲、PTY 历史块缓冲、日志广播（SignalR + 事件总线）、
/// 玩家进退解析。注册为 DI 单例。线程安全（底层为 ConcurrentQueue）。
/// </summary>
public class InstanceConsoleService
{
    /// <summary>每个实例最多保留的日志行数</summary>
    public const int MaxLogLines = 1000;

    /// <summary>每个实例最多保留的 PTY 历史数据块数</summary>
    public const int MaxPtyHistoryChunks = 100;

    private readonly IHubContext<InstanceConsoleHub> _hubContext;
    private readonly IMSLXEvents _events;

    public InstanceConsoleService(IHubContext<InstanceConsoleHub> hubContext, IMSLXEvents events)
    {
        _hubContext = hubContext;
        _events = events;
    }

    #region 缓冲存取

    /// <summary>
    /// 追加一行日志到实例的日志缓冲，超出 <see cref="MaxLogLines"/> 时丢弃最旧的行
    /// </summary>
    public void AppendLog(ServerContext context, string line)
    {
        context.Logs.Enqueue(line);
        while (context.Logs.Count > MaxLogLines)
            context.Logs.TryDequeue(out _);
    }

    /// <summary>
    /// 追加一块 PTY 原始输出到历史缓冲，超出 <see cref="MaxPtyHistoryChunks"/> 时丢弃最旧的块
    /// </summary>
    public void AppendPtyHistory(ServerContext context, string chunk)
    {
        context.PtyHistory.Enqueue(chunk);
        while (context.PtyHistory.Count > MaxPtyHistoryChunks)
            context.PtyHistory.TryDequeue(out _);
    }

    /// <summary>获取实例当前的日志缓冲快照，实例不存在时返回空列表</summary>
    public List<string> GetLogs(ServerContext? context)
        => context?.Logs.ToList() ?? new List<string>();

    /// <summary>获取实例当前的 PTY 历史缓冲快照，实例不存在时返回空列表</summary>
    public List<string> GetPtyHistory(ServerContext? context)
        => context?.PtyHistory.ToList() ?? new List<string>();

    #endregion

    #region 日志记录与广播

    /// <summary>
    /// 记录一行实例日志：写入缓冲、SignalR 推送、发布事件、解析玩家进退。
    /// 空行/纯空白行会被忽略。
    /// </summary>
    public void RecordLog(uint instanceId, ServerContext context, string? data)
    {
        if (string.IsNullOrWhiteSpace(data)) return;

        AppendLog(context, data);

        // 通过 SignalR 推送日志
        _hubContext.Clients.Group(instanceId.ToString()).SendAsync("ReceiveLog", data);

        _events.PublishServerLogReceived(new ServerLogEventArgs
        {
            InstanceId = instanceId,
            LogLine = data,
            IsStdErr = false,
            Timestamp = DateTime.Now
        });

        ParsePlayerActivity(instanceId, context, data);
    }

    // 解析玩家进入/离开日志
    private void ParsePlayerActivity(uint instanceId, ServerContext context, string logLine)
    {
        // 预检
        if (!context.MonitorPlayers)
            return;

        var activity = PlayerActivityParser.Parse(logLine);

        // 玩家加入
        if (activity.Type == PlayerActivityType.Joined)
        {
            if (context.OnlinePlayers.TryAdd(activity.PlayerName, true))
            {
                _hubContext.Clients.Group(instanceId.ToString()).SendAsync("PlayerJoined", instanceId, activity.PlayerName);
            }

            try
            {
                var serverInfo = IConfigBase.ServerList.GetServer(instanceId);
                if (serverInfo != null && !string.IsNullOrEmpty(serverInfo.Base))
                {
                    PlayerActivityTracker.RecordLogin(serverInfo.Base, activity.PlayerName, activity.PlayerIp);
                }
            }
            catch
            {
                // 记录失败就算了 不管他
            }

            return;
        }

        // 玩家离开
        if (activity.Type == PlayerActivityType.Left)
        {
            if (context.OnlinePlayers.TryRemove(activity.PlayerName, out _))
            {
                _hubContext.Clients.Group(instanceId.ToString()).SendAsync("PlayerLeft", instanceId, activity.PlayerName);
            }
        }
    }

    #endregion
}
