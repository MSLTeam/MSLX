using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using MSLX.Daemon.Services;
using MSLX.Daemon.Utils.ConfigUtils;
using System.Security.Claims;

namespace MSLX.Daemon.Hubs;

[Authorize]
public class FrpConsoleHub : Hub
{
    private readonly FrpProcessService _frpService;
    
    public FrpConsoleHub(FrpProcessService frpService)
    {
        _frpService = frpService;
    }

    /// <summary>
    /// 鉴权
    /// </summary>
    private bool HasPermission(int frpId)
    {
        var user = Context.User;
        if (user == null) return false;
        
        var role = user.FindFirst(ClaimTypes.Role)?.Value 
                   ?? user.FindFirst("Role")?.Value 
                   ?? user.FindFirst("role")?.Value;

        if (role == "admin") return true;
        
        var userId = user.FindFirst("UserId")?.Value ?? "";
        if (string.IsNullOrEmpty(userId)) return false;

        return IConfigBase.UserList.HasResourcePermission(userId, "frp", frpId);
    }

    public async Task JoinGroup(int frpId)
    {
        if (!HasPermission(frpId))
        {
            throw new HubException("未找到资源");
        }

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
        if (!HasPermission(frpId))
        {
            throw new HubException("未找到资源");
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, frpId.ToString());
    }
}