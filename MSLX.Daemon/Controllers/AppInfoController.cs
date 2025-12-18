using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models;
using Newtonsoft.Json.Linq;
using MSLX.Daemon.Utils;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Authorization;

namespace MSLX.Daemon.Controllers;

[ApiController]
public class AppInfoController : ControllerBase
{
    [HttpGet("api/status")]
    public IActionResult GetStatus()
    {
        // 获取中间件截取的用户名
        var currentUserId = User.FindFirst("UserId")?.Value;
        
        string displayName = "未登录用户";
        string displayAvatar = "https://www.mslmc.cn/logo.png";
        var roles = new List<string>();

        if (!string.IsNullOrEmpty(currentUserId))
        {
            var userInfo = ConfigServices.UserList.GetUserById(currentUserId);
            if (userInfo != null)
            {
                displayName = !string.IsNullOrEmpty(userInfo.Name) ? userInfo.Name : userInfo.Username;
                displayAvatar = userInfo.Avatar;

                // 处理权限
                if (string.Equals(userInfo.Role, "admin", StringComparison.OrdinalIgnoreCase))
                {
                    roles.Add("all");
                }
                else
                {
                    roles.Add("user");
                }
            }
            
            string osType;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                osType = "Windows";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                osType = "Linux";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                osType = "macOS";
            }
            else
            {
                osType = RuntimeInformation.OSDescription;
            }

            string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";

            var systemInfo = new JObject
            {
                ["netVersion"] = RuntimeInformation.FrameworkDescription,
                ["osType"] = osType,
                ["osVersion"] = RuntimeInformation.OSDescription,
                ["osArchitecture"] = RuntimeInformation.OSArchitecture.ToString(),
                ["hostname"] = Environment.MachineName
            };
            
            var statusData = new JObject
            {
                ["clientName"] = "MSLX Daemon",
                ["version"] = PlatFormServices.GetFormattedVersion(),
                ["id"] = currentUserId,
                ["user"] = displayName,  
                ["username"] = userInfo?.Username ?? "mslx",
                ["avatar"] = displayAvatar,
                ["roles"] = JToken.FromObject(roles),
                ["userIp"] = clientIp,
                ["serverTime"] = DateTime.Now,
                ["targetFrontendVersion"] = new JObject
                {
                    ["desktop"] = "0.0.0",
                    ["panel"] = "0.3.0"
                },
                ["systemInfo"] = systemInfo
            };

            var response = new ApiResponse<JObject>
            {
                Code = 200,
                Message = "MSLX Daemon 状态正常",
                Data = statusData
            };
            return Ok(response);

        }
        else
        {
            return BadRequest(new ApiResponse<object>()
            {
                Code = 400,
                Message = "用户信息错误",
            });
        }
    }
    
    [HttpGet("api/ping")]
    [AllowAnonymous]
    public IActionResult Ping()
    {
        return Ok(new ApiResponse<object>
        {
            Code = 200,
            Message = "pong"
        });
    }
}