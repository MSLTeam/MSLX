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

        // 已加载插件
        foreach (var p in _pluginManager.Plugins)
        {
            var dllPath = p.Assembly.Location;
            processedPaths.Add(dllPath);

            var status = "已启用";
            if (System.IO.File.Exists(dllPath + ".delete"))
            {
                status = "下次重启删除";
            }
            else if (System.IO.File.Exists(dllPath + ".new"))
            {
                status = "下次重启更新";
            }
            else if (System.IO.File.Exists(dllPath + ".disabled"))
            {
                status = "下次重启禁用";
            }

            resultList.Add(new
            {
                id = p.Metadata.Id,
                name = p.Metadata.Name,
                version = p.Metadata.Version,
                developer = p.Metadata.Developer,
                entryPath = $"/plugins/{p.Metadata.Id.ToLower()}/{p.Metadata.Version.ToLower()}/mslx-plugin-entry.js",
                status = status
            });
        }

        // 处理未加载
        if (Directory.Exists(pluginsPath))
        {
            // 查找已禁用插件 (*.dll.disabled)
            foreach (var disabledFile in Directory.GetFiles(pluginsPath, "*.dll.disabled"))
            {
                var baseDllPath = disabledFile.Substring(0, disabledFile.Length - 9); 
                
                if (processedPaths.Add(baseDllPath))
                {
                    var fileName = Path.GetFileNameWithoutExtension(baseDllPath);
                    resultList.Add(new
                    {
                        id = ConvertFileNameToId(fileName),
                        name = fileName,
                        version = "未知",
                        developer = "未知",
                        entryPath = "",
                        status = "已禁用"
                    });
                }
            }

            // 查找纯纯的新插件 (*.dll.new)
            foreach (var newFile in Directory.GetFiles(pluginsPath, "*.dll.new"))
            {
                var baseDllPath = newFile.Substring(0, newFile.Length - 4);
                
                if (processedPaths.Add(baseDllPath))
                {
                    var fileName = Path.GetFileNameWithoutExtension(baseDllPath);
                    resultList.Add(new
                    {
                        id = ConvertFileNameToId(fileName),
                        name = fileName,
                        version = "待读取",
                        developer = "待读取",
                        entryPath = "",
                        status = "下次重启安装"
                    });
                }
            }

            // 查找存在 dll 但没被加载的插件
            foreach (var dllFile in Directory.GetFiles(pluginsPath, "*.dll"))
            {
                if (processedPaths.Add(dllFile))
                {
                    var fileName = Path.GetFileNameWithoutExtension(dllFile);
                    
                    var status = System.IO.File.Exists(dllFile + ".delete") ? "下次重启删除(未加载)" : "下次重启安装";

                    resultList.Add(new
                    {
                        id = ConvertFileNameToId(fileName),
                        name = fileName, 
                        version = "未知",
                        developer = "未知",
                        entryPath = "",
                        status = status
                    });
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

    // 文件名转id
    private string ConvertFileNameToId(string fileName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(fileName)) return fileName;
            
            var parts = fileName.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
            {
                return string.Join("-", parts).ToLower();
            }

            return fileName.ToLower(); 
        }
        catch
        {
            return fileName; 
        }
    }
}