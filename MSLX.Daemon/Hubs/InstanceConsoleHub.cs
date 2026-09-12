using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using MSLX.Daemon.Services;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.IServices;

namespace MSLX.Daemon.Hubs
{
    [Authorize]
    public class InstanceConsoleHub : Hub
    {
        private readonly IMCServerService _mcServerService;

        public InstanceConsoleHub(IMCServerService mcServerService)
        {
            _mcServerService = mcServerService;
        }

        /// <summary>
        /// 内部辅助方法：鉴权
        /// </summary>
        private bool HasPermission(uint instanceId)
        {
            var user = Context.User;
            if (user == null) return false;
            
            var role = user.FindFirst(ClaimTypes.Role)?.Value 
                       ?? user.FindFirst("Role")?.Value 
                       ?? user.FindFirst("role")?.Value;

            if (role == "admin")
            {
                return true;
            }
            
            var userId = user.FindFirst("UserId")?.Value ?? "";
            
            if (string.IsNullOrEmpty(userId)) return false;

            return IConfigBase.UserList.HasResourcePermission(userId, "server", (int)instanceId);
        }

        /// <summary>
        /// 加入服务器控制台组
        /// </summary>
        public async Task JoinGroup(uint instanceId)
        {
            if (!HasPermission(instanceId))
            {
                throw new HubException("未找到实例资源");
            }

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

            // 推送当前 PTY 配置与运行状态
            await GetPtyStatus(instanceId);
        }

        /// <summary>
        /// 离开服务器控制台组
        /// </summary>
        public async Task LeaveGroup(uint instanceId)
        {
            if (!HasPermission(instanceId))
            {
                throw new HubException("未找到实例资源");
            }

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, instanceId.ToString());
        }

        /// <summary>
        /// 发送命令到服务器
        /// </summary>
        public async Task SendCommand(uint instanceId, string command)
        {
            if (!HasPermission(instanceId))
            {
                await Clients.Caller.SendAsync("CommandResult", new { success = false, message = "未找到实例资源" });
                return;
            }

            if (string.IsNullOrWhiteSpace(command))
            {
                await Clients.Caller.SendAsync("CommandResult", new { success = false, message = "命令不能为空" });
                return;
            }

            bool success = _mcServerService.SendCommand(instanceId, command, true);
            await Clients.Caller.SendAsync("CommandResult", new { success, message = success ? "命令已发送" : "发送失败，服务器可能未运行" });
        }

        /// <summary>
        /// 加入服务器 PTY 伪终端会话组
        /// </summary>
        public async Task JoinPtyGroup(uint instanceId, int cols, int rows)
        {
            if (!HasPermission(instanceId))
            {
                throw new HubException("未找到实例资源");
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, "pty_" + instanceId);

            bool isPty = _mcServerService.IsServerPtyMode(instanceId);
            bool isRunning = _mcServerService.IsServerRunning(instanceId);

            // 先调整 PTY 伪终端至客户端当前真实尺寸（并记录首选尺寸）
            if (cols > 0 && rows > 0)
            {
                _mcServerService.ResizePty(instanceId, cols, rows);
            }

            // 回放最近的 PTY 输出历史给刚加入的客户端
            var ptyHistory = _mcServerService.GetPtyHistory(instanceId);
            if (ptyHistory.Any())
            {
                foreach (var chunk in ptyHistory)
                {
                    await Clients.Caller.SendAsync("ReceivePtyData", chunk);
                }
            }

            await Clients.Caller.SendAsync("PtyStatus", new { isPty, isRunning });
        }

        /// <summary>
        /// 离开服务器 PTY 伪终端会话组
        /// </summary>
        public async Task LeavePtyGroup(uint instanceId)
        {
            if (!HasPermission(instanceId))
            {
                throw new HubException("未找到实例资源");
            }

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, "pty_" + instanceId);
        }

        /// <summary>
        /// 发送原始 PTY 输入数据流（按键、转义字符等）
        /// </summary>
        public async Task SendPtyInput(uint instanceId, string data)
        {
            if (!HasPermission(instanceId) || string.IsNullOrEmpty(data))
            {
                return;
            }

            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(data);
            bool success = _mcServerService.SendPtyInput(instanceId, bytes);
            if (!success)
            {
                // 如果未在 PTY 模式且按下了回车，兼容性降级为普通命令行发送
                if (!_mcServerService.IsServerPtyMode(instanceId) && (data.Contains('\r') || data.Contains('\n')))
                {
                    string clean = data.Trim('\r', '\n');
                    if (!string.IsNullOrWhiteSpace(clean))
                    {
                        _mcServerService.SendCommand(instanceId, clean, true);
                    }
                }
            }
        }

        /// <summary>
        /// 调整 PTY 伪终端窗口尺寸
        /// </summary>
        public void ResizePty(uint instanceId, int cols, int rows)
        {
            if (!HasPermission(instanceId) || cols <= 0 || rows <= 0) return;
            _mcServerService.ResizePty(instanceId, cols, rows);
        }

        /// <summary>
        /// 查询实例 PTY 状态
        /// </summary>
        public async Task GetPtyStatus(uint instanceId)
        {
            if (!HasPermission(instanceId)) return;
            bool isPty = _mcServerService.IsServerPtyMode(instanceId);
            bool isRunning = _mcServerService.IsServerRunning(instanceId);
            var server = IConfigBase.ServerList.GetServer(instanceId);
            bool enablePty = server?.EnablePty ?? false;
            await Clients.Caller.SendAsync("PtyStatus", new { isPty, isRunning, enablePty });
        }
    }
}