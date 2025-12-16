using Microsoft.AspNetCore.SignalR;
using MSLX.Daemon.Services;

namespace MSLX.Daemon.Hubs
{
    public class InstanceConsoleHub : Hub
    {
        private readonly MCServerService _mcServerService;

        public InstanceConsoleHub(MCServerService mcServerService)
        {
            _mcServerService = mcServerService;
        }

        /// <summary>
        /// 加入服务器控制台组
        /// </summary>
        public async Task JoinGroup(uint instanceId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, instanceId.ToString());

            // 获取历史日志
            var historyLogs = _mcServerService.GetLogs(instanceId);

            // 推送历史日志给当前客户端
            if (historyLogs.Any())
            {
                foreach (var log in historyLogs)
                {
                    await Clients.Caller.SendAsync("ReceiveLog", log);
                }
            }
        }

        /// <summary>
        /// 离开服务器控制台组
        /// </summary>
        public async Task LeaveGroup(uint instanceId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, instanceId.ToString());
        }

        /// <summary>
        /// 发送命令到服务器
        /// </summary>
        public async Task SendCommand(uint instanceId, string command)
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                await Clients.Caller.SendAsync("CommandResult", new { success = false, message = "命令不能为空" });
                return;
            }

            bool success = _mcServerService.SendCommand(instanceId, command,true);
            await Clients.Caller.SendAsync("CommandResult", new { success, message = success ? "命令已发送" : "发送失败，服务器可能未运行" });
        }
    }
}