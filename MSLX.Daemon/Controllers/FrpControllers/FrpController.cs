using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models;
using MSLX.Daemon.Models.Frp;
using MSLX.Daemon.Services;
using MSLX.Daemon.Utils.ConfigUtils;
using Newtonsoft.Json.Linq;
using Tomlyn;
using Tomlyn.Model;

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
        var configList = IConfigBase.FrpList.ReadFrpList();
        
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
        }).Reverse().ToList();
        
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
    
    
    [HttpGet("info")]
    public IActionResult GetFrpInfo([FromQuery] int id)
    {
        // 您在运行吗。jpg
        bool isRunning = _frpService.IsFrpRunning(id);
        var response = new FrpInfoResponse
        {
            IsRunning = isRunning,
        };

        // 获取配置
        var frpConfigMeta = IConfigBase.FrpList.GetFrpConfig(id);
        if (frpConfigMeta == null)
        {
            return NotFound(new ApiResponse<object> { Code = 404, Message = "找不到该配置" });
        }

        string configType = frpConfigMeta["ConfigType"]?.ToString() ?? "ini";

        // 暂时只支持toml喵
        if (configType.ToLower() != "toml")
        {
            return Ok(new ApiResponse<FrpInfoResponse>
            {
                Code = 200,
                Data = response,
                Message = "非 TOML 格式，无法解析详情"
            });
        }

        // 读取文件
        string configPath = Path.Combine(IConfigBase.GetAppDataPath(), "DaemonData", "Configs", "Frpc", id.ToString(), $"frpc.toml");
        if (!System.IO.File.Exists(configPath))
        {
            // 说实话 这能丢？
            return Ok(new ApiResponse<FrpInfoResponse>
            {
                Code = 200, 
                Data = response, 
                Message = "配置文件丢失" 
            });
        }

        try
        {
            // 这里是解析toml的
            string tomlContent = System.IO.File.ReadAllText(configPath);
            var model = Toml.ToModel(tomlContent);
            
            string serverAddr = "未知";
            if (model.TryGetValue("serverAddr", out var addrObj))
            {
                serverAddr = addrObj.ToString()!;
            }

            // MSLFrp特有的域名字段
            string? remoteDomain = null;
            if (model.TryGetValue("metadatas", out var metaObj) && metaObj is TomlTable metaTable)
            {
                if (metaTable.TryGetValue("mslFrpRemoteDomain", out var domainObj))
                {
                    remoteDomain = domainObj.ToString();
                }
            }

            // 获取proxies列表（一般就一个 但是可能存在自定义多个隧道的问题）
            if (model.TryGetValue("proxies", out var proxiesObj) && proxiesObj is TomlTableArray proxiesArray)
            {
                foreach (var proxyItem in proxiesArray)
                {
                    string name = proxyItem.TryGetValue("name", out var n) ? n.ToString()! : "Unknown";
                    string type = proxyItem.TryGetValue("type", out var t) ? t.ToString()! : "Unknown";
                    string localIp = proxyItem.TryGetValue("localIP", out var lip) ? lip.ToString()! : "127.0.0.1";
                    string localPort = proxyItem.TryGetValue("localPort", out var lp) ? lp.ToString()! : "?";
                    string remotePort = proxyItem.TryGetValue("remotePort", out var rp) ? rp.ToString()! : "?";
                    
                    // 联机类型隧道特化处理
                    if (type == "xtcp")
                    {
                        serverAddr = proxyItem.TryGetValue("secretKey", out var sk) ? sk.ToString()! : "Unknown";
                    }
                    
                    // 构造地址
                    string mainHost = !string.IsNullOrEmpty(remoteDomain) ? remoteDomain : serverAddr;
                    
                    var detail = new ProxyDetail
                    {
                        ProxyName = name,
                        Type = type,
                        LocalAddress = $"{localIp}:{localPort}",
                        RemoteAddressMain = type != "xtcp" ? $"{mainHost}:{remotePort}" : $"{mainHost}",
                        RemoteAddressBackup = type != "xtcp" ? $"{serverAddr}:{remotePort}" : $"{serverAddr}"
                    };

                    response.Proxies.Add(detail);
                }
            }
                            
            // 获取visitors列表 (联机的访客)
            if (model.TryGetValue("visitors", out var visitorsObj) && visitorsObj is TomlTableArray visitorsArray)
            {
                foreach (var visitorItem in visitorsArray)
                {
                    string name = visitorItem.TryGetValue("serverName", out var n) ? n.ToString()! : "Unknown";
                    string type = visitorItem.TryGetValue("type", out var t) ? t.ToString()! : "xtcp";
                    string localIp = visitorItem.TryGetValue("bindAddr", out var lip)
                        ? lip.ToString()!
                        : "127.0.0.1";
                    string localPort = visitorItem.TryGetValue("bindPort", out var lp) ? lp.ToString()! : "?";
                    string remotePort = visitorItem.TryGetValue("remotePort", out var rp) ? rp.ToString()! : "?";
                    serverAddr = visitorItem.TryGetValue("secretKey", out var sk) ? sk.ToString()! : "Unknown";

                    // 构造地址
                    string mainHost = !string.IsNullOrEmpty(remoteDomain) ? remoteDomain : serverAddr;

                    var detail = new ProxyDetail
                    {
                        ProxyName = name,
                        Type = $"{type} - Visitors",
                        LocalAddress = $"{localIp}:{localPort}",
                        RemoteAddressMain = $"{mainHost}",
                        RemoteAddressBackup = $"{serverAddr}"
                    };

                    response.Proxies.Add(detail);
                }
            }
        }
        catch (Exception ex)
        {
            // 空列表
            return Ok(new ApiResponse<FrpInfoResponse>
            {
                Code = 200,
                Data = response, 
                Message = $"配置文件解析失败: {ex.Message}"
            });
        }

        return Ok(new ApiResponse<FrpInfoResponse>
        {
            Code = 200,
            Message = "获取成功",
            Data = response
        });
    }
    
    
}