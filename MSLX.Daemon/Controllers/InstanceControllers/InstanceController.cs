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
    public IActionResult StartInstance([FromBody] ServerActionRequest request)
    {
        /* 参数已经在模型完成验证 无需再验证一次
        if (!request.ID.HasValue)
        {
            return Ok(ApiResponseService.CreateResponse(400, "服务器ID无效"));
        } */
        switch (request.Action)
        {
            case "start":
                var (result, msg) = _mcServerService.StartServer(request.ID.Value);

                return result
                    ? Ok(ApiResponseService.Success(msg))
                    : Ok(ApiResponseService.Error("服务器开启失败：" + msg));
            case "stop":
                bool suc = _mcServerService.StopServer(request.ID.Value);
                return suc
                    ? Ok(ApiResponseService.Success("已发送停止服务器指令"))
                    : Ok(ApiResponseService.Error("服务器停止失败"));
            case "backup":
                bool backup = _mcServerService.StartBackupServer(request.ID.Value);
                return backup
                    ? Ok(ApiResponseService.Success("已开始备份···"))
                    : Ok(ApiResponseService.Error("服务器可能不在运行，启动备份失败！"));
            default:
                // 按道理模型拦截了 这里走不到的
                return Ok(ApiResponseService.Error("无效的操作"));
        }
    }
}