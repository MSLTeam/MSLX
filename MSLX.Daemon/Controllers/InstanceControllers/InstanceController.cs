using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Services;
using MSLX.Daemon.Utils;
using Newtonsoft.Json.Linq;

namespace MSLX.Daemon.Controllers.InstanceControllers
{
    [ApiController]
    public class InstanceController : ControllerBase
    {
        private readonly MCServerService _mcServerService;
        public InstanceController(MCServerService mcServerService)
        {
            _mcServerService = mcServerService;
        }

        [HttpPost("api/instance/start")]
        public async Task<IActionResult> StartInstance([FromQuery] int? id)
        {
            if (id == null)
            {
                return BadRequest(new { message = "Empty ID." });
            }
            var result = await _mcServerService.StartServer((int)id);
            if (result)
            {
                return Ok(ApiResponseService.CreateResponse<JObject>(200, "Instance started."));
            }
            else
            {
                return Ok(ApiResponseService.CreateResponse<JObject>(400, "Failed to start instance."));
            }
        }

    }
}