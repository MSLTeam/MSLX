using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models.Instance;
using MSLX.Daemon.Services;
using MSLX.Daemon.Utils;

namespace MSLX.Daemon.Controllers.InstanceControllers;

[Route("api/instance")]
[ApiController]
public class InstanceController : ControllerBase
{
    private readonly MCServerService _mcServerService;

    public InstanceController(MCServerService mcServerService)
    {
        _mcServerService = mcServerService;
    }

    [HttpPost("start")]
    public async Task<IActionResult> StartInstance([FromBody] LaunchServerRequest request)
    {
        if (!request.ID.HasValue)
        {
            return Ok(ApiResponseService.CreateResponse(400, "服务器ID无效"));
        }

        var (Result, Msg) = _mcServerService.StartServer(request.ID.Value);

        return Result
            ? Ok(ApiResponseService.Success(Msg))
            : Ok(ApiResponseService.Error("Failed to start instance." + Msg));
    }
}