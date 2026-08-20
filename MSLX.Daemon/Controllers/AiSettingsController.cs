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
                AiModelName = (string?)(config["aiModelName"]) ?? "deepseek-chat"
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
}
