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

    [HttpPost("action")]
    public async Task<IActionResult> StartInstance([FromBody] ServerActionRequest request)
    {
        /* 参数已经在模型完成验证 无需再验证一次
        if (!request.ID.HasValue)
        {
            return Ok(ApiResponseService.CreateResponse(400, "服务器ID无效"));
        } */
        if (request.Action == "start")
        {
            var (Result, Msg) = _mcServerService.StartServer(request.ID.Value);

            return Result
                ? Ok(ApiResponseService.Success(Msg))
                : Ok(ApiResponseService.Error("服务器开启失败：" + Msg));
        }
        else
        {
            bool suc = _mcServerService.StopServer(request.ID.Value);
            return suc
                ? Ok(ApiResponseService.Success("已发送停止服务器指令"))
                : Ok(ApiResponseService.Error("服务器停止失败"));
        }

        
    }
}