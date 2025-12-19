using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models;
using MSLX.Daemon.Models.Frp;
using MSLX.Daemon.Services;
using MSLX.Daemon.Utils.ConfigUtils;
using Newtonsoft.Json.Linq;

namespace MSLX.Daemon.Controllers.FrpControllers;

[ApiController]
[Route("api/frp")]
public class CreateFrpController : ControllerBase
{
    private readonly FrpProcessService _frpService;
    public CreateFrpController(FrpProcessService frpService)
    {
        _frpService = frpService;
    }
    
    [HttpPost("add")]
    public IActionResult CreateTunnel([FromBody] CreateFrpRequest request)
    {
        bool suc = IConfigBase.FrpList.CreateFrpConfig(request.name, request.provider, request.format,
            request.config);
        var response = new ApiResponse<JObject>
        {
            Code = suc ? 200 : 500,
            Message = suc ? $"隧道 {request.name} 成功添加！" : "隧道创建失败！", 
        };

        return Ok(response);
    }
    
    [HttpPost("delete")]
    public IActionResult DeleteTunnel([FromBody] DeleteFrpRequest request)
    { 
        bool running = _frpService.IsFrpRunning(request.id);
        if (running)
        {
            return BadRequest(new ApiResponse<JObject>
            {
                Code = 400,
                Message = "请先停止该隧道！"
            });
        }
        bool suc = IConfigBase.FrpList.DeleteFrpConfig(request.id);
        var response = new ApiResponse<JObject>
        {
            Code = suc ? 200 : 400,
            Message = suc ? $"隧道 {request.id} 删除成功！" : "删除失败！", 
        };
        return suc  ? Ok(response) : BadRequest(response);
    }
}