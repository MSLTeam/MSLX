using Microsoft.AspNetCore.SignalR;
using MSLX.Daemon.Services;

namespace MSLX.Daemon.Hubs;

public class FrpConsoleHub : Hub
{
    private readonly FrpProcessService _frpService;
    
    public FrpConsoleHub(FrpProcessService frpService)
    {
        _frpService = frpService;
    }

    public async Task JoinGroup(int frpId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, frpId.ToString());

        // 历史日志
        var historyLogs = _frpService.GetLogs(frpId);

        // 推送历史日志
        if (historyLogs.Any())
        {
            foreach (var log in historyLogs)
            {
                await Clients.Caller.SendAsync("ReceiveLog", log);
            }
        }
    }

    public async Task LeaveGroup(int frpId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, frpId.ToString());
    }
}