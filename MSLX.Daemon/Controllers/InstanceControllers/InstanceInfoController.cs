using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models;
using MSLX.Daemon.Utils;
using Newtonsoft.Json.Linq;

namespace MSLX.Daemon.Controllers.InstanceControllers
{
    [ApiController]
    public class InstanceInfoController : ControllerBase
    {

        [HttpGet("api/instance/list")]
        public IActionResult GetInstanceList()
        {
            ConfigServices.ServerListConfig config = ConfigServices.ServerList;
            var serverList = config.ReadServerList();
            if (serverList!=null)
            {
                var response = new ApiResponse<JObject>
                {
                    Code = 200,
                    Message = "ok",
                    Data = JObject.FromObject(new { list = serverList })
                };

                return Ok(response);
            }
            else
            {
                return BadRequest(new { message = "Failed!" });
            }
        }

    }
}