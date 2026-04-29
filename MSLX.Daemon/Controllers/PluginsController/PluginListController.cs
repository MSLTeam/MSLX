using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models;
using MSLX.Daemon.Services;

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
                version = p.Metadata.Version,
                developer = p.Metadata.Developer,
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