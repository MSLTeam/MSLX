using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models;
using MSLX.Daemon.Services;
using MSLX.Daemon.Utils;
using Newtonsoft.Json.Linq;

namespace MSLX.Daemon.Controllers.InstanceControllers;

[Route("api/instance")]
[ApiController]
public class InstanceInfoController : ControllerBase
{
    private readonly MCServerService _mcServerService;
    public InstanceInfoController(MCServerService mcServerService)
    {
        _mcServerService = mcServerService;
    }
    
    [HttpGet("list")]
    public IActionResult GetInstanceList()
    {
        ConfigServices.ServerListConfig config = ConfigServices.ServerList;
        var serverList = config.ReadServerList();
        var resultList = serverList.Select(item => 
        {
            uint id = item["ID"]?.Value<uint>() ?? 0;
            bool isRunning = _mcServerService.IsServerRunning(id);

            return new 
            {
                id,
                name = item["Name"]?.Value<string>(),
                basePath = item["Base"]?.Value<string>(),
                java = item["Java"]?.Value<string>(),
                core = item["Core"]?.Value<string>(),
                icon = "https://bbs-static.miyoushe.com/static/2024/12/05/0e139c6c04de1a23ea1400819574b59c_3322869863064933822.gif", // icon 暂时写死 日后再改
                status = isRunning,
            };
        }).OrderByDescending(x => x.id).ToList();
        
        return Ok(new ApiResponse<object>
        {
            Code = 200,
            Message = "获取成功",
            Data = resultList
        });
    }

    [HttpGet("info")]
    public IActionResult GetInstanceInfo(uint id)
    {
        try
        {
            McServerInfo.ServerInfo server =
                ConfigServices.ServerList.GetServer(id) ?? throw new Exception("找不到指定的服务器");
            return Ok(new ApiResponse<object>
            {
                Code = 200,
                Message = "获取成功",
                Data = new
                {
                    id = server.ID,
                    name = server.Name,
                    basePath = server.Base,
                    java = server.Java,
                    minM = server.MinM,
                    maxM = server.MaxM,
                    core = server.Core,
                    status = _mcServerService.IsServerRunning(id),
                }
            });
        }catch (Exception e)
        {
            return BadRequest(new ApiResponse<object>
            {
                Code = 400,
                Message = e.Message
            });
        }
    }
}