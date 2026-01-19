using Microsoft.AspNetCore.SignalR;

namespace MSLX.Daemon.Hubs;

public class DaemonUpdateHub : Hub
{
    public async Task JoinGroup(string serverId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, serverId);
    }

    public async Task LeaveGroup(string serverId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, serverId);
    }
}