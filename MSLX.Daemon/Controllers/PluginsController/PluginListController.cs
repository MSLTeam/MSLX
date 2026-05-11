using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Services;
using MSLX.SDK.Models;
using MSLX.Daemon.Utils.ConfigUtils;

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
        var resultList = new List<object>();
        var pluginsPath = Path.Combine(IConfigBase.GetAppDataPath(), "Plugins"); 
        
        var processedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 已加载
        foreach (var p in _pluginManager.Plugins)
        {
            var dllPath = p.Assembly.Location;
            processedPaths.Add(dllPath);

            var status = "已启用";
            if (System.IO.File.Exists(dllPath + ".delete")) status = "下次重启删除";
            else if (System.IO.File.Exists(dllPath + ".new")) status = "下次重启更新";
            else if (System.IO.File.Exists(dllPath + ".disabled")) status = "下次重启禁用";
            var iconPath = (p.Metadata.Icon != null && p.Metadata.Icon.StartsWith("http")) 
                ? "https://www.mslmc.cn/logo.png" 
                : $"/plugins/{p.Metadata.Id.ToLower()}/{p.Metadata.Version.ToLower()}/{p.Metadata.Icon}";

            resultList.Add(new
            {
                id = p.Metadata.Id,
                name = p.Metadata.Name,
                description = p.Metadata.Description,
                icon = iconPath,
                version = p.Metadata.Version,
                minSDKVersion = p.Metadata.MinSDKVersion,
                developer = p.Metadata.Developer,
                authorUrl = p.Metadata.AuthorUrl,
                pluginUrl = p.Metadata.PluginUrl,
                entryPath = $"/plugins/{p.Metadata.Id.ToLower()}/{p.Metadata.Version.ToLower()}/mslx-plugin-entry.js",
                path = dllPath,
                status = status
            });
        }

        // 未加载
        if (Directory.Exists(pluginsPath))
        {
            // 查找已禁用插件 (*.dll.disabled)
            foreach (var disabledFile in Directory.GetFiles(pluginsPath, "*.dll.disabled"))
            {
                var baseDllPath = disabledFile.Substring(0, disabledFile.Length - 9); 
                if (processedPaths.Add(baseDllPath))
                {
                    resultList.Add(CreateUnloadedPluginObj(baseDllPath, "已禁用", "未知"));
                }
            }

            // 查找纯新插件 (*.dll.new)
            foreach (var newFile in Directory.GetFiles(pluginsPath, "*.dll.new"))
            {
                var baseDllPath = newFile.Substring(0, newFile.Length - 4);
                if (processedPaths.Add(baseDllPath))
                {
                    resultList.Add(CreateUnloadedPluginObj(baseDllPath, "下次重启安装", "待读取"));
                }
            }

            // 查找存在 dll 但没被加载的插件
            foreach (var dllFile in Directory.GetFiles(pluginsPath, "*.dll"))
            {
                if (processedPaths.Add(dllFile))
                {
                    var status = System.IO.File.Exists(dllFile + ".delete") ? "下次重启删除(未加载)" : "未加载";
                    resultList.Add(CreateUnloadedPluginObj(dllFile, status, "未知"));
                }
            }
        }

        return Ok(new ApiResponse<object>
        {
            Code = 200,
            Message = "插件列表获取成功",
            Data = resultList
        });
    }
    
    private object CreateUnloadedPluginObj(string dllPath, string status, string placeholderText)
    {
        var fileName = Path.GetFileNameWithoutExtension(dllPath);
        return new
        {
            id = ConvertFileNameToId(fileName),
            name = fileName,
            description = placeholderText,
            icon = "https://www.mslmc.cn/logo.png",
            version = placeholderText,
            minSDKVersion = placeholderText,
            developer = placeholderText,
            authorUrl = "",
            pluginUrl = "",
            entryPath = "",
            path = dllPath,
            status = status
        };
    }

    // 文件名转ID
    private string ConvertFileNameToId(string fileName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(fileName)) return fileName;
            var parts = fileName.Split('.', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 ? string.Join("-", parts).ToLower() : fileName.ToLower();
        }
        catch { return fileName; }
    }
}