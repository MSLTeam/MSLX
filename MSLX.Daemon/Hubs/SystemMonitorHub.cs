using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace MSLX.Daemon.Hubs;

[Authorize(Roles = "admin")]
public class SystemMonitorHub : Hub
{
    public const string GroupName = "SystemMonitorClients";

    /// <summary>
    /// 订阅监控数据
    /// </summary>
    public async Task JoinMonitor()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName);
    }

    /// <summary>
    /// 取消订阅
    /// </summary>
    public async Task LeaveMonitor()
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName);
    }
}