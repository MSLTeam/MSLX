using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models;
using MSLX.Daemon.Utils;
using Newtonsoft.Json.Linq;
using MSLX.Daemon.Models.Instance; 

namespace MSLX.Daemon.Controllers.InstanceControllers;

[ApiController]
public class CreateInstanceController : ControllerBase
{
    [HttpPost("api/instance/createServer")]
    public IActionResult CreateServer([FromBody] CreateServerRequest request)
    {
        var serverId = ConfigServices.ServerList.GenerateServerId();
        
        McServerInfo.ServerInfo server = new McServerInfo.ServerInfo
        {
            ID = serverId,
            Name = request.name, 
            Base = request.path ?? Path.Combine(ConfigServices.GetAppDataPath(),"DaemonData","Servers",serverId.ToString()), 
            Java = request.java ?? "java", 
            Core = request.core, 
            MinM = request.minM,
            MaxM = request.maxM,
            Args = request.args ?? ""
        };

        ConfigServices.ServerList.CreateServer(server);
        
        var response = new ApiResponse<JObject> 
        {
            code = 200,
            message = "服务器创建成功",
            data = new JObject
            {
                ["serverId"] = serverId
            }
        };
        
        return Ok(response);
    }
}
