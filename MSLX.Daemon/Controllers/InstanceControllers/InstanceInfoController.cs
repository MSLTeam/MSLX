using System.Text;
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
            string icon = "default";

            if ((item["Core"]?.Value<string>() ?? "").Contains("neoforge"))
            {
                icon = "neoforge";
            }else if ((item["Core"]?.Value<string>() ?? "").Contains("forge"))
            {
                icon = "forge";
            }else if((item["Core"]?.Value<string>() ?? "") == "none")
            {
                icon = "custom";
            }
            try
            {
                if (Directory.Exists(item["Base"]?.Value<string>()))
                {
                    if (System.IO.File.Exists(Path.Combine(item["Base"]?.Value<string>() ?? "", "server-icon.png")))
                    {
                        icon = "server-icon";
                    }
                }
            }catch{}

            return new 
            {
                id,
                name = item["Name"]?.Value<string>(),
                basePath = item["Base"]?.Value<string>(),
                java = item["Java"]?.Value<string>(),
                core = item["Core"]?.Value<string>(),
                icon,
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
            if (System.IO.File.Exists(Path.Combine(server.Base, "server.properties")))
            {
                dynamic config = ServerPropertiesLoader.Load(Path.Combine(server.Base, "server.properties"),Encoding.GetEncoding(server.FileEncoding));
                string difficulty = "未知";
                switch (config.difficulty)
                {
                    case "easy":
                        difficulty = "简单";
                        break;
                    case "normal":
                        difficulty = "普通";
                        break;
                    case "hard":
                        difficulty = "困难";
                        break;
                    case "peaceful":
                        difficulty = "和平";
                        break;
                    default:
                        difficulty = "未知";
                        break;
                }
                string gamemode = "未知";
                switch (config.gamemode)
                {
                    case "survival":
                        gamemode = "生存";
                        break;
                    case "creative":
                        gamemode = "创造";
                        break;
                    case "adventure":
                        gamemode = "冒险";
                        break;
                    case "spectator":
                        gamemode = "旁观";
                        break;
                    default:
                        gamemode = "未知";
                        break;
                }
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
                        uptime = _mcServerService.GetServerUptime(id),
                        mcConfig = new
                        {
                            difficulty,
                            gamemode,
                            levelName = config.level_name,
                            serverPort = config.server_port,
                            onlineMode = config.online_mode,
                        }
                    }
                });
            }
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
                    uptime = _mcServerService.GetServerUptime(id),
                    mcConfig = new
                    {
                        difficulty = "未知",
                        gamemode = "未知",
                        levelName = "未知",
                        serverPort = "未知",
                        onlineMode = "未知",
                    }
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
    
    [HttpGet("icon/{id}.png")]
    public IActionResult GetInstanceIcon(uint id)
    { 
        try
        {
            McServerInfo.ServerInfo server =
                ConfigServices.ServerList.GetServer(id) ?? throw new Exception("找不到指定的服务器");
            string iconPath = Path.Combine(server.Base, "server-icon.png");
            if (!System.IO.File.Exists(iconPath))
            {
                return NotFound(new ApiResponse<object>()
                {
                    Code = 404,
                    Message = "该服务器没有图标"
                });
            }
            Response.Headers.Append("Cache-Control", "public, max-age=3600");
            return PhysicalFile(iconPath, "image/png");
        }
        catch (Exception e)
        {
            return BadRequest(new ApiResponse<object>()
            {
                Code = 400,
                Message = e.Message
            });
        }
    }
}