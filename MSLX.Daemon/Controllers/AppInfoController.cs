using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models;
using Newtonsoft.Json.Linq;
using System.Reflection;
using MSLX.Daemon.Utils;
using System.Runtime.InteropServices;

namespace MSLX.Daemon.Controllers
{
    [ApiController] 
    public class AppInfoController : ControllerBase 
    {
        [HttpGet("api/status")]
        public IActionResult GetStatus()
        {
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
                // 其他系统，使用详细描述
                osType = RuntimeInformation.OSDescription; 
            }

            // 获取来访者 IP
            string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1"; 

            // 系统信息
            var systemInfo = new JObject
            {
                ["netVersion"] = RuntimeInformation.FrameworkDescription, // NET环境版本
                ["osType"] = osType, // 系统类型
                ["osVersion"] = RuntimeInformation.OSDescription,
                ["osArchitecture"] = RuntimeInformation.OSArchitecture.ToString(), // 系统架构
                ["hostname"] = Environment.MachineName // 主机名
            };
            

            var statusData = new JObject
            {
                ["clientName"] = "MSLX Daemon",
                ["version"] = PlatFormServices.GetFormattedVersion(),
                //["version"] = Assembly.GetEntryAssembly()?.GetName()?.Version?.ToString() ?? "0.0.0.0",
                ["user"] = ConfigServices.Config.ReadConfigKey("user")?.ToString() ?? "MSLX User",
                ["avatar"] = ConfigServices.Config.ReadConfigKey("avatar")?.ToString() ?? "https://www.mslmc.cn/logo.png",
                ["userIp"] = clientIp,
                ["serverTime"] = DateTime.Now,
                ["targetFrontendVersion"] = new JObject
                {
                    ["desktop"] = "0.0.0",
                    ["panel"] = "0.1.1"
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
    }
}