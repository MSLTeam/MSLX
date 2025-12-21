using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models.Instance;
using MSLX.Daemon.Services;
using MSLX.Daemon.Utils;
using System.Threading.Tasks;

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
        string requestAction = request.Action;
        string requestQuery = string.Empty;
        string[] parts = requestAction.Split('?'); // get query
        requestAction = parts[0];
        if (parts.Length > 1)
        {
            requestQuery = parts[1];
        }

        switch (requestAction)
        {
            case "start":
                var (result, msg) = _mcServerService.StartServer(request.ID!.Value);

                return result
                    ? Ok(ApiResponseService.Success(msg))
                    : Ok(ApiResponseService.Error("服务器开启失败：" + msg));
            case "stop":
                bool suc = _mcServerService.StopServer(request.ID!.Value);
                return suc
                    ? Ok(ApiResponseService.Success("已发送停止服务器指令"))
                    : Ok(ApiResponseService.Error("服务器停止失败"));
            case "agreeEula":
                bool isAgree = bool.Parse(requestQuery);
                bool ageula = await _mcServerService.AgreeEULA(request.ID!.Value, isAgree);
                return ageula
                    ? Ok(ApiResponseService.Success("执行成功"))
                    : Ok(ApiResponseService.Error("执行失败"));
            case "backup":
                bool backup = _mcServerService.StartBackupServer(request.ID!.Value);
                return backup
                    ? Ok(ApiResponseService.Success("已开始备份···"))
                    : Ok(ApiResponseService.Error("服务器可能不在运行，启动备份失败！"));
            default:
                // 按道理模型拦截了 这里走不到的
                return Ok(ApiResponseService.Error("无效的操作"));
        }
    }
}