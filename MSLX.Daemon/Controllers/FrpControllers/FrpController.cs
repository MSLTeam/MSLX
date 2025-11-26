using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models;
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
}