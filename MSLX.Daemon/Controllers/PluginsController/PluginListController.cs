using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Services;
using MSLX.SDK.Models;

namespace MSLX.Daemon.Controllers.PluginsController;

[ApiController]
[Route("api/plugins")]
public class PluginListController : ControllerBase
{
    private readonly PluginManager _pluginManager;

    public PluginListController(PluginManager pluginManager)
    {
        _pluginManager = pluginManager;
    }

    [HttpGet("list")]
    public IActionResult GetPluginList()
    {
        var resultList = _pluginManager.Plugins.Select(p => new
            {
                id = p.Metadata.Id,
                name = p.Metadata.Name,
                description = p.Metadata.Description,
                icon = p.Metadata.Icon.StartsWith("http") ? "https://www.mslmc.cn/logo.png" : $"/plugins/{p.Metadata.Id.ToLower()}/{p.Metadata.Version.ToLower()}/{p.Metadata.Icon}",
                version = p.Metadata.Version,
                minSDKVersion = p.Metadata.MinSDKVersion,
                developer = p.Metadata.Developer,
                authorUrl = p.Metadata.AuthorUrl,
                pluginUrl = p.Metadata.PluginUrl,
                entryPath = $"/plugins/{p.Metadata.Id.ToLower()}/{p.Metadata.Version.ToLower()}/mslx-plugin-entry.js"
            })
            .ToList();

        return Ok(new ApiResponse<object>
        {
            Code = 200,
            Message = "插件列表获取成功",
            Data = resultList
        });
    }
}