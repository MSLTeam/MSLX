using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models;
using Newtonsoft.Json.Linq;
using System.Reflection;

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
                ["serverTime"] = DateTime.Now
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