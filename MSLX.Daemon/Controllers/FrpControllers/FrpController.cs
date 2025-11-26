using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models;
using MSLX.Daemon.Models.Frp;
using MSLX.Daemon.Services;
using MSLX.Daemon.Utils;
using Newtonsoft.Json.Linq;

namespace MSLX.Daemon.Controllers.FrpControllers;

[ApiController]
[Route("api/frp")]
public class FrpController : ControllerBase
{
    private readonly FrpProcessService _frpService;

    // 注入进程服务
    public FrpController(FrpProcessService frpService)
    {
        _frpService = frpService;
    }

    [HttpGet("list")]
    public IActionResult GetFrpList()
    {
        var configList = ConfigServices.FrpList.ReadFrpList();
        
        var resultList = configList.Select(item => 
        {
            int id = item["ID"]?.Value<int>() ?? 0;
            bool isRunning = _frpService.IsFrpRunning(id);

            return new 
            {
                id,
                name = item["Name"]?.Value<string>(),
                service = item["Service"]?.Value<string>(),
                configType = item["ConfigType"]?.Value<string>(),
                status = isRunning,
            };
        }).ToList();
        
        return Ok(new ApiResponse<object>
        {
            Code = 200,
            Message = "获取成功",
            Data = resultList
        });
    }
    
    [HttpPost("action")]
    public IActionResult OperatingFrp([FromBody] FrpActionRequest request)
    {
        if (request.Action == "start")
        {
            var (success, msg) = _frpService.StartFrp(request.Id);
            return Ok(new ApiResponse<object>
            {
                Code = success ? 200 : 500,
                Message = msg
            });
        }
        else if (request.Action == "stop")
        {
            bool success = _frpService.StopFrp(request.Id);
            return Ok(new ApiResponse<object>
            {
                Code = success ? 200 : 500,
                Message = success ? "已停止" : "停止失败或未运行"
            });
        }
        
        return BadRequest(new ApiResponse<object> { Code = 400, Message = "未知操作" });
    }
    
    
}