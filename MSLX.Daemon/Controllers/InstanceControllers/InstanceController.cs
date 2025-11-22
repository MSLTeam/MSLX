using Downloader;
using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models.Instance;
using MSLX.Daemon.Services;
using MSLX.Daemon.Utils;

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
        public async Task<IActionResult> StartInstance([FromBody] LaunchServerRequest request)
        {
            if (!request.ID.HasValue)
            {
                return Ok(ApiResponseService.CreateResponse(400, "服务器ID无效"));
            }
            var result = await _mcServerService.StartServer(request.ID.Value);

            return result
                ? Ok(ApiResponseService.Success("Instance started."))
                : Ok(ApiResponseService.Error("Failed to start instance."));
        }
    }
}