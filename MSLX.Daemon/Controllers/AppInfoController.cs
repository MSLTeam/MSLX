using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models;
using Newtonsoft.Json.Linq;
using System.Reflection;
using MSLX.Daemon.Utils;

namespace MSLX.Daemon.Controllers
{
    [ApiController] 
    public class AppInfoController : ControllerBase 
    {
        [HttpGet("api/status")]
        public IActionResult GetStatus()
        {
            var statusData = new JObject
            {
                ["clientName"] = "MSLX Daemon",
                ["version"] = Assembly.GetEntryAssembly()?.GetName()?.Version?.ToString() ?? "0.0.0.0",
                ["user"] = ConfigServices.Config.ReadConfigKey("user")?.ToString() ?? "MSLX User",
                ["avatar"] = ConfigServices.Config.ReadConfigKey("avatar")?.ToString() ?? "https://www.mslmc.cn/logo.png",
                ["serverTime"] = DateTime.Now,
                ["targetFrontendVersion"] = new JObject
                {
                    ["desktop"] = "1.0.0",
                    ["panel"] = "1.0.0"
                }
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