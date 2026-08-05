using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.Models;

namespace MSLX.Daemon.Controllers;

[ApiController]
[Route("api/settings/ai")]
public class AiSettingsController : ControllerBase
{
    [HttpGet]
    [Authorize(Roles = "admin")]
    public IActionResult GetAiSettings()
    {
        var config = IConfigBase.Config.ReadConfig();
        return Ok(new ApiResponse<object>
        {
            Code = 200,
            Message = "获取成功",
            Data = new
            {
                AiEnabled = (bool?)(config["aiEnabled"]) ?? false,
                AiApiKey = (string?)(config["aiApiKey"]) ?? "",
                AiBaseUrl = (string?)(config["aiBaseUrl"]) ?? "https://api.deepseek.com/v1",
                AiModelName = (string?)(config["aiModelName"]) ?? "deepseek-chat",
                AiSystemPrompt = (string?)(config["aiSystemPrompt"]) ?? "你是一个专业的 Minecraft 服务器运维助手。当用户提出创建、配置修改、状态查询或控制服务器的需求时，分析用户的要求并优先使用工具函数(Function Calling)完成操作。"
            }
        });
    }

    [HttpPost]
    [Authorize(Roles = "admin")]
    public IActionResult UpdateAiSettings([FromBody] AiSettingsRequest request)
    {
        IConfigBase.Config.WriteConfigKey("aiEnabled", request.AiEnabled);
        IConfigBase.Config.WriteConfigKey("aiApiKey", request.AiApiKey ?? "");
        IConfigBase.Config.WriteConfigKey("aiBaseUrl", request.AiBaseUrl ?? "https://api.deepseek.com/v1");
        IConfigBase.Config.WriteConfigKey("aiModelName", request.AiModelName ?? "deepseek-chat");
        IConfigBase.Config.WriteConfigKey("aiSystemPrompt", request.AiSystemPrompt ?? "");

        return Ok(new ApiResponse<object>
        {
            Code = 200,
            Message = "AI 配置更新成功"
        });
    }
}

public class AiSettingsRequest
{
    public bool AiEnabled { get; set; }
    public string? AiApiKey { get; set; }
    public string? AiBaseUrl { get; set; }
    public string? AiModelName { get; set; }
    public string? AiSystemPrompt { get; set; }
}
