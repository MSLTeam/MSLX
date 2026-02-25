using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models;
using MSLX.Daemon.Services;

namespace MSLX.Daemon.Controllers.InstanceControllers;

[Route("api/instance/players")]
[ApiController]
public class PlayerManagerController : ControllerBase
{
    private readonly MCServerService _mcServerService;

    public PlayerManagerController(MCServerService mcServerService)
    {
        _mcServerService = mcServerService;
    }
    [HttpGet("online/{id}")]
    public IActionResult GetOnlinePlayers(uint id)
    {
        try
        {
            var players = _mcServerService.GetOnlinePlayers(id);
            return Ok(new ApiResponse<object>
            {
                Code = 200,
                Message = "获取成功",
                Data = players
            });
        }
        catch (Exception e)
        {
            return BadRequest(new ApiResponse<object>
            {
                Code = 400,
                Message = e.Message
            });
        }
    }
}

