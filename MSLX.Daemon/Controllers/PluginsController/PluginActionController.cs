using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Services;
using MSLX.Daemon.Services.PluginsService;
using MSLX.SDK.Models;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.Models.Plugins;

namespace MSLX.Daemon.Controllers.PluginsController;



[ApiController]
[Route("api/plugins")]
public class PluginActionController : ControllerBase
{
    private readonly PluginManager _pluginManager;
    private readonly string _pluginsPath;

    public PluginActionController(PluginManager pluginManager)
    {
        _pluginManager = pluginManager;
        _pluginsPath = Path.Combine(IConfigBase.GetAppDataPath(), "Plugins");
    }

    [HttpPost("action")]
    [Authorize(Roles = "admin")]
    public IActionResult HandleAction([FromBody] PluginActionRequest request)
    {
        var dllPath = GetDllPathById(request.Id);
        
        if (string.IsNullOrEmpty(dllPath) && request.Action.ToLower() != "cancel")
        {
            return Error("找不到对应的插件文件");
        }

        return request.Action.ToLower() switch
        {
            "disable" => DisablePlugin(dllPath),
            "enable"  => EnablePlugin(dllPath),
            "delete"  => DeletePlugin(dllPath),
            "cancel"  => CancelPendingAction(request.Id, dllPath),
            _         => Error("未知的操作类型")
        };
    }
    

    private IActionResult DisablePlugin(string dllPath)
    {
        var disabledMarker = dllPath + ".disabled";
        try
        {
            if (!System.IO.File.Exists(disabledMarker))
            {
                System.IO.File.WriteAllText(disabledMarker, "");
            }
            
            _pluginManager.UnloadPlugin(dllPath);
            
            return Success("插件已成功禁用并卸载，立即生效");
        }
        catch (Exception ex)
        {
            return Error($"禁用操作失败: {ex.Message}");
        }
    }

    private IActionResult EnablePlugin(string dllPath)
    {
        var disabledMarker = dllPath + ".disabled";
        if (System.IO.File.Exists(disabledMarker))
        {
            System.IO.File.Delete(disabledMarker);
        }

        var isRunning = _pluginManager.Plugins.Any(p => p.DllPath.Equals(dllPath, StringComparison.OrdinalIgnoreCase));
        if (!isRunning)
        {
            var success = _pluginManager.LoadPlugin(dllPath);
            if (success)
                return Success("插件已成功启用并加载生效");
            else
                return Error("插件启用失败，请查看后台日志");
        }

        return Success("插件已恢复正常启用状态");
    }

    private IActionResult DeletePlugin(string dllPath)
    {
        var deleteMarker = dllPath + ".delete";
        try
        {
            // 卸载插件，释放文件占用
            _pluginManager.UnloadPlugin(dllPath);

            // 如果存在禁用标记，顺便清理掉，以删除标记为最高优先级
            var disabledMarker = dllPath + ".disabled";
            if (System.IO.File.Exists(disabledMarker)) System.IO.File.Delete(disabledMarker);

            if (!System.IO.File.Exists(deleteMarker))
            {
                System.IO.File.WriteAllText(deleteMarker, "");
            }
            
            if (System.IO.File.Exists(dllPath))
            {
                try { 
                    System.IO.File.Delete(dllPath); 
                    System.IO.File.Delete(deleteMarker);
                    return Success("插件已成功删除并卸载！");
                } 
                catch 
                { 
                    return Success("插件已标记为删除，将在下次重启时彻底删除文件"); 
                }
            }
            return Success("插件已移除");
        }
        catch (Exception ex)
        {
            return Error($"删除操作失败: {ex.Message}");
        }
    }

    private IActionResult CancelPendingAction(string id, string dllPath)
    {
        if (string.IsNullOrEmpty(dllPath))
        {
            dllPath = Path.Combine(_pluginsPath, id + ".dll");
        }

        try
        {
            if (System.IO.File.Exists(dllPath + ".delete")) System.IO.File.Delete(dllPath + ".delete");
            if (System.IO.File.Exists(dllPath + ".new")) System.IO.File.Delete(dllPath + ".new"); 
            if (System.IO.File.Exists(dllPath + ".disabled")) System.IO.File.Delete(dllPath + ".disabled");

            return Success("已成功撤销所有待处理的变更操作");
        }
        catch (Exception ex)
        {
            return Error($"撤销操作失败: {ex.Message}");
        }
    }
    

    private string GetDllPathById(string id)
    {
        var loadedPlugin = _pluginManager.Plugins.FirstOrDefault(p => 
            p.Metadata.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        
        if (loadedPlugin != null) return loadedPlugin.DllPath;
        
        if (Directory.Exists(_pluginsPath))
        {
            var allDlls = Directory.GetFiles(_pluginsPath, "*.dll");
            foreach (var dll in allDlls)
            {
                var fileName = Path.GetFileNameWithoutExtension(dll);
                var convertedId = ConvertFileNameToId(fileName);
                if (convertedId.Equals(id, StringComparison.OrdinalIgnoreCase))
                {
                    return dll;
                }
            }
        }
        return string.Empty;
    }

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

    private IActionResult Success(string message) => Ok(new ApiResponse<object> { Code = 200, Message = message });
    private IActionResult Error(string message) => Ok(new ApiResponse<object> { Code = 400, Message = message });
}