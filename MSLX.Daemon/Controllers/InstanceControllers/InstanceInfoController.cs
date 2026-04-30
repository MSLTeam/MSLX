using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Services;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.IServices;
using Newtonsoft.Json.Linq;
using MSLX.SDK.Models;

namespace MSLX.Daemon.Controllers.InstanceControllers;

[Route("api/instance")]
[ApiController]
public class InstanceInfoController : ControllerBase
{
    private readonly IMCServerService _mcServerService;
    public InstanceInfoController(IMCServerService mcServerService)
    {
        _mcServerService = mcServerService;
    }
    
[HttpGet("list")]
    public IActionResult GetInstanceList()
    {
        var userId = User?.FindFirst("UserId")?.Value ?? "";
        var currentUser = IConfigBase.UserList.GetUserById(userId);
        
        if (currentUser == null) 
            return Unauthorized(new ApiResponse<object> { Code = 401, Message = "用户不存在或登录已过期" });

        bool isAdmin = currentUser.Role == "admin";
        
        var allowedServerIds = new HashSet<uint>();
        if (!isAdmin && currentUser.Resources != null)
        {
            foreach (var res in currentUser.Resources)
            {
                if (res.StartsWith("server:") && uint.TryParse(res.Substring(7), out uint resId))
                {
                    allowedServerIds.Add(resId);
                }
            }
        }

        ServerListConfig config = IConfigBase.ServerList;
        var serverList = config.ReadServerList();
        
        var resultList = serverList
            .Where(item => 
            {
                uint id = item["ID"]?.Value<uint>() ?? 0;
                return isAdmin || allowedServerIds.Contains(id);
            })
            .Select(item => 
            {
                uint id = item["ID"]?.Value<uint>() ?? 0;
                var (serverStatus, serverStatusText) = _mcServerService.GetServerStatus(id);
                string icon = "default";

                if ((item["Core"]?.Value<string>() ?? "").Contains("neoforge"))
                {
                    icon = "neoforge";
                }
                else if ((item["Core"]?.Value<string>() ?? "").Contains("forge"))
                {
                    icon = "forge";
                }
                else if ((item["Core"]?.Value<string>() ?? "") == "none")
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
                }
                catch { }

                return new 
                {
                    id,
                    name = item["Name"]?.Value<string>(),
                    basePath = item["Base"]?.Value<string>(),
                    java = item["Java"]?.Value<string>(),
                    core = item["Core"]?.Value<string>(),
                    icon,
                    status = serverStatus,
                    statusText = serverStatusText,
                    extra = new
                    {
                        onlinePlayers = _mcServerService.GetOnlinePlayers(id).Count,
                    }
                };
            })
            .OrderByDescending(x => x.id)
            .ToList();
        
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
        if (!IConfigBase.UserList.HasResourcePermission(User?.FindFirst("UserId")?.Value ?? "", "server", (int)id))
            return NotFound(ApiResponseService.NotFound());
        try
        {
            McServerInfo.ServerInfo server =
                IConfigBase.ServerList.GetServer(id) ?? throw new Exception("找不到指定的服务器");
            var (serverStatus, serverStatusText) = _mcServerService.GetServerStatus(id);
            if (System.IO.File.Exists(Path.Combine(server.Base, "server.properties")))
            {
                dynamic config = ServerPropertiesLoader.Load(Path.Combine(server.Base, "server.properties"),FileUtils.GetFileEncodingByString(server.FileEncoding));
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
                        args = server.Args,
                        minM = server.MinM,
                        maxM = server.MaxM,
                        core = server.Core,
                        status = serverStatus,
                        statusText = serverStatusText,
                        uptime = _mcServerService.GetServerUptime(id),
                        monitorPlayers = server.MonitorPlayers,
                        onlinePlayers = _mcServerService.GetOnlinePlayers(id).Count,
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
                    status = serverStatus,
                    statusText = serverStatusText,
                    uptime = _mcServerService.GetServerUptime(id),
                    monitorPlayers = server.MonitorPlayers,
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
        if (!IConfigBase.UserList.HasResourcePermission(User?.FindFirst("UserId")?.Value ?? "", "server", (int)id))
            return NotFound(ApiResponseService.NotFound());
        try
        {
            McServerInfo.ServerInfo server =
                IConfigBase.ServerList.GetServer(id) ?? throw new Exception("找不到指定的服务器");
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