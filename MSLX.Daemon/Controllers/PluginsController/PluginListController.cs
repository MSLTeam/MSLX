using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Services.NodeServices;
using MSLX.Daemon.Services.PluginsService;
using MSLX.Daemon.Utils;
using MSLX.SDK.Models;
using MSLX.Daemon.Utils.ConfigUtils;

namespace MSLX.Daemon.Controllers.PluginsController;

[ApiController]
[Route("api/plugins")]
public class PluginListController : ControllerBase
{
    private readonly PluginManager _pluginManager;
    private readonly PluginPackageService _packages;
    private readonly NodeAuthorization _nodeAuthorization;
    private bool _includeSyncInfo;

    public PluginListController(PluginManager pluginManager, PluginPackageService packages, NodeAuthorization nodeAuthorization)
    {
        _pluginManager = pluginManager;
        _packages = packages;
        _nodeAuthorization = nodeAuthorization;
    }

    private bool IsPublicAccessEnabled(string pluginId)
    {
        var val = IConfigBase.Config.ReadConfigKey($"Plugins:PublicAccess:{pluginId}");
        if (val != null && bool.TryParse(val.ToString(), out var parsed)) return parsed;
        return false;
    }

    private (bool isAuthenticated, bool isAdmin) GetAuthInfo()
    {
        // Token 检查
        string? token = Request.Headers["x-user-token"].FirstOrDefault()
                     ?? Request.Query["x-user-token"].FirstOrDefault()
                     ?? Request.Query["token"].FirstOrDefault();

        if (!string.IsNullOrEmpty(token))
        {
            var principal = JwtUtils.ValidateToken(token);
            if (principal != null && !principal.HasClaim(c => c.Type == "Scope"))
            {
                var userId = principal.FindFirst("UserId")?.Value;

                if (userId == "system-admin")
                {
                    return (true, true);
                }

                if (!string.IsNullOrEmpty(userId))
                {
                    var dbUser = IConfigBase.UserList.GetUserById(userId);
                    if (dbUser != null && JwtUtils.GetTokenRejectionReason(principal, dbUser) == null)
                    {
                        bool isAdmin = dbUser.Role == "admin";
                        return (true, isAdmin);
                    }
                }
            }
        }

        // ApiKey 检查
        string? apiKey = Request.Headers["x-api-key"].FirstOrDefault()
                      ?? Request.Query["x-api-key"].FirstOrDefault();

        if (!string.IsNullOrEmpty(apiKey))
        {
            string globalApiKey = IConfigBase.Config.ReadConfigKey("apiKey")?.ToString() ?? "";
            if (!string.IsNullOrEmpty(globalApiKey) && globalApiKey.Equals(apiKey))
            {
                return (true, true);
            }

            var user = IConfigBase.UserList.GetUserByApiKey(apiKey);
            if (user != null)
            {
                return (true, user.Role == "admin");
            }
        }

        return (false, false);
    }

    private object FormatPluginDto(LoadedPlugin p, bool includePath = false)
    {
        var iconPath = p.Metadata.Icon switch
        {
            null or "" => "https://www.mslmc.cn/logo.png",
            var icon when icon.StartsWith("http", StringComparison.OrdinalIgnoreCase) => icon,
            var icon => $"/plugins/{p.Metadata.Id.ToLower()}/{p.Metadata.Version.ToLower()}/{icon}"
        };

        var status = "已启用";
        if (System.IO.File.Exists(p.DllPath + ".delete")) status = "待删除";

        if (includePath)
        {
            return new
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
                path = p.DllPath,
                status = status,
                supportsMultiNode = p.Metadata.SupportsMultiNode,
                frontendRevision = System.IO.File.GetLastWriteTimeUtc(p.DllPath).Ticks.ToString(),
                syncInfo = _includeSyncInfo ? _packages.Describe(p.Metadata.Id) : null,
                hasPublicPages = p.Metadata.HasPublicPages,
                isPublicAccessEnabled = IsPublicAccessEnabled(p.Metadata.Id)
            };
        }

        return new
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
            status = status,
            supportsMultiNode = p.Metadata.SupportsMultiNode,
            frontendRevision = System.IO.File.GetLastWriteTimeUtc(p.DllPath).Ticks.ToString(),
            hasPublicPages = p.Metadata.HasPublicPages,
            isPublicAccessEnabled = IsPublicAccessEnabled(p.Metadata.Id)
        };
    }

    [HttpGet("list")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPluginList([FromQuery] bool includeSyncInfo = false)
    {
        bool isAuthenticated, isAdmin;
        var nodeId = Request.Headers["x-node-id"].FirstOrDefault();
        if (!string.IsNullOrEmpty(nodeId) && (bool?)IConfigBase.Config.ReadConfigKey("IsSlaveMode") == true)
        {
            try
            {
                var lease = await _nodeAuthorization.VerifyAsync(nodeId, Request.Headers["x-user-token"].ToString(), HttpContext.RequestAborted);
                if (lease.Principal.HasClaim(c => c.Type == "Scope")) return Unauthorized(new ApiResponse<object> { Code = 401, Message = "插件列表需要普通节点凭据" });
                NodeAuthorization.Apply(HttpContext, lease);
                isAuthenticated = true;
                isAdmin = User.IsInRole("admin");
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
            {
                var code = ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden } ? 401 : 503;
                return StatusCode(code, new ApiResponse<object> { Code = code, Message = code == 401 ? "节点授权已失效" : "主节点暂时不可用" });
            }
        }
        else (isAuthenticated, isAdmin) = GetAuthInfo();
        _includeSyncInfo = isAdmin && includeSyncInfo;

        if (!isAuthenticated)
        {
            // 匿名访客：仅返回开启公开页面的插件
            var publicList = _pluginManager.Plugins
                .Where(p => p.Metadata.HasPublicPages && IsPublicAccessEnabled(p.Metadata.Id))
                .Where(p => !System.IO.File.Exists(p.DllPath + ".disabled") && !System.IO.File.Exists(p.DllPath + ".delete"))
                .Select(p => FormatPluginDto(p, includePath: false))
                .ToList();

            return Ok(new ApiResponse<object>
            {
                Code = 200,
                Message = "公开插件列表获取成功",
                Data = publicList
            });
        }

        if (!isAdmin)
        {
            // 普通登录用户
            var userPluginList = _pluginManager.Plugins
                .Where(p => !System.IO.File.Exists(p.DllPath + ".disabled") && !System.IO.File.Exists(p.DllPath + ".delete"))
                .Select(p => FormatPluginDto(p, includePath: false))
                .ToList();

            return Ok(new ApiResponse<object>
            {
                Code = 200,
                Message = "插件列表获取成功",
                Data = userPluginList
            });
        }

        var resultList = new List<object>();
        var pluginsPath = Path.Combine(IConfigBase.GetAppDataPath(), "Plugins"); 
        
        var processedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var p in _pluginManager.Plugins)
        {
            processedPaths.Add(p.DllPath);
            resultList.Add(FormatPluginDto(p, includePath: true));
        }

        // 未加载的插件
        if (Directory.Exists(pluginsPath))
        {
            foreach (var dllFile in Directory.GetFiles(pluginsPath, "*.dll"))
            {
                if (!dllFile.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
                if (processedPaths.Contains(dllFile)) continue;

                // 标记
                bool hasDeleteMarker = System.IO.File.Exists(dllFile + ".delete");
                bool hasDisabledMarker = System.IO.File.Exists(dllFile + ".disabled");
                bool hasNewMarker = System.IO.File.Exists(dllFile + ".new");
                
                string status = "未加载";
                if (hasDeleteMarker) status = "待删除";
                else if (hasDisabledMarker) status = "已禁用";
                else if (hasNewMarker) status = "待安装";

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
                    resultList.Add(CreateUnloadedPluginObj(baseDllPath, "已禁用", "未知"));
                }
            }
            foreach (var newFile in Directory.GetFiles(pluginsPath, "*.dll.new"))
            {
                var baseDllPath = newFile.Substring(0, newFile.Length - 4);
                if (!processedPaths.Contains(baseDllPath))
                {
                    processedPaths.Add(baseDllPath);
                    resultList.Add(CreateUnloadedPluginObj(baseDllPath, "待安装", "待读取"));
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
        var metadata = _pluginManager.GetPluginMetadata(dllPath);
        
        if (metadata != null)
        {
            return new
            {
                id = metadata.Id,
                name = metadata.Name,
                description = metadata.Description,
                icon = metadata.Icon switch
                {
                    null or "" => "https://www.mslmc.cn/logo.png",
                    var icon when icon.StartsWith("http", StringComparison.OrdinalIgnoreCase) => icon,
                    _ => "https://www.mslmc.cn/logo.png"
                },
                version = metadata.Version,
                minSDKVersion = metadata.MinSDKVersion,
                developer = metadata.Developer,
                authorUrl = metadata.AuthorUrl ?? "",
                pluginUrl = metadata.PluginUrl ?? "",
                entryPath = $"/plugins/{metadata.Id.ToLower()}/{metadata.Version.ToLower()}/mslx-plugin-entry.js",
                path = dllPath,
                status = status,
                supportsMultiNode = metadata.SupportsMultiNode,
                frontendRevision = System.IO.File.GetLastWriteTimeUtc(dllPath).Ticks.ToString(),
                syncInfo = _includeSyncInfo && System.IO.File.Exists(dllPath) ? _packages.Describe(metadata.Id) : null,
                hasPublicPages = metadata.HasPublicPages,
                isPublicAccessEnabled = IsPublicAccessEnabled(metadata.Id)
            };
        }

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
            status = status,
            supportsMultiNode = MSLX.SDK.Models.Plugins.PluginMultiNodeSupport.NoTest,
            hasPublicPages = false,
            isPublicAccessEnabled = false
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
