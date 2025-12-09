using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models;
using MSLX.Daemon.Services;
using MSLX.Daemon.Utils;

namespace MSLX.Daemon.Controllers.InstanceControllers;

[Route("api/instance/settings")]
[ApiController]
public class InstanceSettingsController: ControllerBase
{
    private readonly MCServerService _mcServerService;
    public InstanceSettingsController(MCServerService mcServerService)
    {
        _mcServerService = mcServerService;
    }
    
    [HttpGet("general/{id}")]
    public IActionResult GetGeneralSettings(uint id)
    {
        try
        {
            McServerInfo.ServerInfo serverInfo = ConfigServices.ServerList.GetServer(id) ?? throw new Exception("未找到服务端实例配置");
            return Ok(new ApiResponse<object>
            {
                Code = 200,
                Message = "获取成功",
                Data = serverInfo
            });
        }catch(Exception e)
        {
            return BadRequest(new ApiResponse<object>
            {
                Code = 400,
                Message = e.Message,
            });
        }
    }
}