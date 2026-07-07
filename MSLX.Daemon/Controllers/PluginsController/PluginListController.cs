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
        
        foreach (var p in _pluginManager.Plugins)
        {
            var dllPath = p.Assembly.Location;
            processedPaths.Add(dllPath);

            var status = "已启用";
            if (System.IO.File.Exists(dllPath + ".delete")) status = "下次重启删除";
            else if (System.IO.File.Exists(dllPath + ".new")) status = "下次重启更新";
            else if (System.IO.File.Exists(dllPath + ".disabled")) status = "下次重启禁用";
            
            var iconPath = p.Metadata.Icon switch
            {
                null or "" => "https://www.mslmc.cn/logo.png",
                var icon when icon.StartsWith("http", StringComparison.OrdinalIgnoreCase) => icon,
                var icon => $"/plugins/{p.Metadata.Id.ToLower()}/{p.Metadata.Version.ToLower()}/{icon}"
            };

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

        // 未加载的插件
        if (Directory.Exists(pluginsPath))
        {
            foreach (var dllFile in Directory.GetFiles(pluginsPath, "*.dll"))
            {
                if (processedPaths.Contains(dllFile)) continue;

                // 标记
                bool hasDeleteMarker = System.IO.File.Exists(dllFile + ".delete");
                bool hasDisabledMarker = System.IO.File.Exists(dllFile + ".disabled");
                bool hasNewMarker = System.IO.File.Exists(dllFile + ".new");
                
                string status = "未加载";
                if (hasDeleteMarker) status = "下次重启删除(未加载)";
                else if (hasDisabledMarker) status = "已禁用";
                else if (hasNewMarker) status = "下次重启安装";

                processedPaths.Add(dllFile);
                resultList.Add(CreateUnloadedPluginObj(dllFile, status, "未知"));
            }
            
            // 处理 只有标记的情况
            foreach (var disabledFile in Directory.GetFiles(pluginsPath, "*.dll.disabled"))
            {
                var baseDllPath = disabledFile.Substring(0, disabledFile.Length - 9); 
                if (!processedPaths.Contains(baseDllPath))
                {
                    processedPaths.Add(baseDllPath);
                    resultList.Add(CreateUnloadedPluginObj(baseDllPath, "已禁用(缺失核心)", "未知"));
                }
            }
            foreach (var newFile in Directory.GetFiles(pluginsPath, "*.dll.new"))
            {
                var baseDllPath = newFile.Substring(0, newFile.Length - 4);
                if (!processedPaths.Contains(baseDllPath))
                {
                    processedPaths.Add(baseDllPath);
                    resultList.Add(CreateUnloadedPluginObj(baseDllPath, "下次重启安装", "待读取"));
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