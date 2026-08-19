using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Utils.ConfigUtils;
using System.IO.Compression; 
using System.Text.Json;  
using System.Text.RegularExpressions;
using MSLX.Daemon.Utils;
using MSLX.SDK.Models;
using MSLX.SDK.Models.Files;

namespace MSLX.Daemon.Controllers.FilesControllers;

[ApiController]
[Route("api/files/pm")]
public class PluginsAndModsController : ControllerBase
{
    [HttpGet("instance/{id}/list")]
    public IActionResult GetPluginsAndModsList(uint id, [FromQuery] string? mode = "plugins", [FromQuery] bool checkClient = false)
    {
        if (!IConfigBase.UserList.HasResourcePermission(User?.FindFirst("UserId")?.Value ?? "", "server", (int)id))
            return NotFound(ApiResponseService.NotFound());
        
        try
        {
            var server = IConfigBase.ServerList.GetServer(id);
            if (server == null)
            {
                return NotFound(new ApiResponse<object>
                {
                    Code = 404,
                    Message = "服务器不存在"
                });
            }

            string targetPath = GetPluginOrModPath(server, mode);
            if (!Directory.Exists(targetPath))
            {
                return NotFound(new ApiResponse<object>
                {
                    Code = 404,
                    Message = $"未检测到配置的{(mode == "plugins" ? "插件" : "模组")}目录,请检查当前服务端是否支持使用{(mode == "plugins" ? "插件" : "模组")}，或者尝试启动一次服务器。",
                });
            }

            // 获取这个文件夹所有模组/插件
            var jarFilesList = Directory.GetFiles(targetPath, "*.jar").Select(Path.GetFileName).ToList();
            var disableJarFiles = Directory.GetFiles(targetPath, "*.jar.disabled").Select(Path.GetFileName).ToArray();
            
            // 存放检测到的客户端模组
            var clientJarFiles = new List<string>();

            // 仅在 mods 模式且开启检测时执行
            if (mode == "mods" && checkClient)
            {
                var modInfos = new List<ModInfo>();
                foreach (var fileName in jarFilesList.ToList())
                {
                    string fullPath = Path.Combine(targetPath, fileName);
                    var info = ParseModInfo(fullPath);
                    info.FileName = fileName;
                    modInfos.Add(info);
                }

                var activeMods = new HashSet<ModInfo>(modInfos);
                bool changed = true;
                while (changed)
                {
                    changed = false;
                    foreach (var info in activeMods.ToList())
                    {
                        if (info.IsClientOnly)
                        {
                            // 检查是否有其他仍处于 active 状态的模组硬依赖它
                            bool isRequiredByActive = activeMods.Any(m => m != info && m.Dependencies.Contains(info.ModId));
                            if (!isRequiredByActive)
                            {
                                // 确认为纯客户端模组
                                activeMods.Remove(info);
                                changed = true;
                            }
                        }
                    }
                }

                foreach (var info in modInfos)
                {
                    if (!activeMods.Contains(info))
                    {
                        clientJarFiles.Add(info.FileName);
                        jarFilesList.Remove(info.FileName);
                    }
                }
            }

            return Ok(new ApiResponse<object>()
            {
                Code = 200,
                Message = "获取成功",
                Data = new
                {
                    totalCount = disableJarFiles.Length + jarFilesList.Count + clientJarFiles.Count,
                    activeCount = jarFilesList.Count,
                    clientOnlyCount = clientJarFiles.Count,
                    disabledCount = disableJarFiles.Length,
                    jarFiles = jarFilesList, 
                    clientJarFiles,
                    disableJarFiles,
                }
            });
        }
        catch (Exception e)
        {
            return BadRequest(new ApiResponse<object>
            {
                Code = 500,
                Message = "服务器内部错误" + e.Message,
            });
        }
    }


    private static string GetPluginOrModPath(McServerInfo.ServerInfo server, string? mode)
    {
        var isMods = string.Equals(mode, "mods", StringComparison.OrdinalIgnoreCase);
        var label = isMods ? "模组" : "插件";
        var relativePath = isMods
            ? ServerPropertiesPathUtils.NormalizeRelativePath(server.ModsPath, "mods", "模组目录路径必须是实例目录内的相对路径")
            : ServerPropertiesPathUtils.NormalizeRelativePath(server.PluginsPath, "plugins", "插件目录路径必须是实例目录内的相对路径");

        var check = FileUtils.GetSafePath(server.Base, relativePath);
        if (!check.IsSafe) throw new ArgumentException($"{label}目录不安全: {check.Message}");
        return check.FullPath;
    }

    /// <summary>
    /// 解析模组的详细信息 (ID, 依赖, 是否为客户端模组)
    /// </summary>
    private ModInfo ParseModInfo(string filePath)
    {
        var info = new ModInfo { FileName = Path.GetFileName(filePath) };
        try
        {
            using var archive = ZipFile.OpenRead(filePath);

            // 常见的纯客户端模组ID（如果某个模组强依赖这些，那它必然也是纯客户端模组）
            var knownClientApiIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "iris", "sodium", "modmenu", "indium", "rubidium", "oculus", "embeddium", 
                "optifabric", "sodium-extra", "cullleaves", "optifine"
            };

            // Fabric (fabric.mod.json) 
            var fabricEntry = archive.GetEntry("fabric.mod.json");
            if (fabricEntry != null)
            {
                using var stream = fabricEntry.Open();
                using var doc = JsonDocument.Parse(stream);
                
                if (doc.RootElement.TryGetProperty("id", out var idElement))
                {
                    info.ModId = idElement.GetString() ?? "";
                }

                // 直接声明为客户端环境
                if (doc.RootElement.TryGetProperty("environment", out var envElement))
                {
                    string? env = envElement.GetString();
                    if ("client".Equals(env, StringComparison.OrdinalIgnoreCase))
                    {
                        info.IsClientOnly = true;
                    }
                }

                // 检查硬依赖项
                if (doc.RootElement.TryGetProperty("depends", out var dependsElement) && dependsElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var dep in dependsElement.EnumerateObject())
                    {
                        info.Dependencies.Add(dep.Name);
                        if (knownClientApiIds.Contains(dep.Name)) info.IsClientOnly = true;
                    }
                }
                return info;
            }

            // Quilt (quilt.mod.json) 应该用的不多 但是还是写上吧
            var quiltEntry = archive.GetEntry("quilt.mod.json");
            if (quiltEntry != null)
            {
                using var stream = quiltEntry.Open();
                using var doc = JsonDocument.Parse(stream);
                
                if (doc.RootElement.TryGetProperty("quilt_loader", out var quiltLoader))
                {
                    if (quiltLoader.TryGetProperty("id", out var idElement))
                    {
                        info.ModId = idElement.GetString() ?? "";
                    }

                    if (quiltLoader.TryGetProperty("metadata", out var metadata) &&
                        metadata.TryGetProperty("environment", out var envElement))
                    {
                        string? env = envElement.GetString();
                        if ("client".Equals(env, StringComparison.OrdinalIgnoreCase) || 
                            "client_only".Equals(env, StringComparison.OrdinalIgnoreCase))
                        {
                            info.IsClientOnly = true;
                        }
                    }

                    if (quiltLoader.TryGetProperty("depends", out var dependsElement) && dependsElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var dep in dependsElement.EnumerateArray())
                        {
                            string? depId = null;
                            if (dep.ValueKind == JsonValueKind.String) depId = dep.GetString();
                            if (dep.ValueKind == JsonValueKind.Object && dep.TryGetProperty("id", out var depIdElement)) depId = depIdElement.GetString();
                            
                            if (depId != null)
                            {
                                info.Dependencies.Add(depId);
                                if (knownClientApiIds.Contains(depId)) info.IsClientOnly = true;
                            }
                        }
                    }
                }
                return info;
            }

            // Forge/NeoForge (META-INF/mods.toml 或 META-INF/neoforge.mods.toml)
            var tomlEntry = archive.GetEntry("META-INF/mods.toml") ?? archive.GetEntry("META-INF/neoforge.mods.toml");
            if (tomlEntry != null)
            {
                using var stream = tomlEntry.Open();
                using var reader = new StreamReader(stream);
                string content = reader.ReadToEnd();

                var modIdMatch = Regex.Match(content, @"^\s*modId\s*=\s*[""'](.*?)[""']", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                if (modIdMatch.Success)
                {
                    info.ModId = modIdMatch.Groups[1].Value.ToLower();
                }

                // 先检查是否有全量显式的客户端标签 (排除注释行 #clientSideOnly=true)
                if (Regex.IsMatch(content, @"(?m)^\s*clientSideOnly\s*=\s*true", RegexOptions.IgnoreCase))
                {
                    info.IsClientOnly = true;
                }

                // 提取所有 dependencies 块
                var blocks = Regex.Matches(content, @"(?ms)^\[\[dependencies\..*?\]\]\s*(.*?)(?=^\[\[|\z)");

                bool foundEngineDependency = false;
                bool isEngineClientOnly = false;

                foreach (Match block in blocks)
                {
                    string blockBody = block.Groups[1].Value;

                    var depIdMatch = Regex.Match(blockBody, @"^\s*modId\s*=\s*[""'](.*?)[""']", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                    if (depIdMatch.Success)
                    {
                        string depId = depIdMatch.Groups[1].Value.ToLower();
                        
                        var mandatoryMatch = Regex.Match(blockBody, @"^\s*mandatory\s*=\s*(true|false)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                        bool isMandatory = mandatoryMatch.Success && mandatoryMatch.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase);
                        
                        if (isMandatory)
                        {
                            info.Dependencies.Add(depId);
                        }

                        // 仅当依赖是核心时，其 side 才有效
                        if (depId == "minecraft" || depId == "forge" || depId == "neoforge")
                        {
                            foundEngineDependency = true;

                            var sideMatch = Regex.Match(blockBody, @"^\s*side\s*=\s*[""'](.*?)[""']", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                            
                            // 如果核心依赖项没有注明 side，则默认为 BOTH（双端）
                            string side = sideMatch.Success ? sideMatch.Groups[1].Value.ToUpper() : "BOTH";

                            if (side == "CLIENT")
                            {
                                isEngineClientOnly = true;
                            }
                            else
                            {
                                // 如果任何一个核心引擎依赖是 BOTH 或 SERVER，那不是纯客户端模组
                                isEngineClientOnly = false;
                            }
                        }
                    }
                }

                if (foundEngineDependency && isEngineClientOnly)
                {
                    info.IsClientOnly = true;
                }
                return info;
            }

            return info;
        }
        catch
        {
            // 如果文件损坏或无法读取，默认为非客户端模组
            return info;
        }
    }

    [HttpPost("instance/{id}/set")]
    public IActionResult SetPluginOrModState(uint id, [FromBody] SetPluginModStateRequest request)
    {
        if (!IConfigBase.UserList.HasResourcePermission(User?.FindFirst("UserId")?.Value ?? "", "server", (int)id))
            return NotFound(ApiResponseService.NotFound());
        
        var server = IConfigBase.ServerList.GetServer(id);
        if (server == null)
            return NotFound(new ApiResponse<object> { Code = 404, Message = "服务器不存在" });

        string targetPath = GetPluginOrModPath(server, request.Mode);
        if (!Directory.Exists(targetPath))
            return NotFound(new ApiResponse<object> { Code = 404, Message = "配置的目录不存在" });

        if (request.Targets == null || request.Targets.Count == 0)
            return BadRequest(new ApiResponse<object> { Code = 400, Message = "请选择至少一个文件" });

        int successCount = 0;
        int failCount = 0;

        foreach (var fileName in request.Targets)
        {
            string currentFilePath = Path.Combine(targetPath, Path.GetFileName(fileName));

            try
            {
                if (!System.IO.File.Exists(currentFilePath))
                {
                    failCount++;
                    continue;
                }

                if (request.Action == "disable")
                {
                    if (Path.GetFileName(fileName).EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                    {
                        string newPath = currentFilePath + ".disabled";
                        if (System.IO.File.Exists(newPath)) System.IO.File.Delete(newPath);

                        System.IO.File.Move(currentFilePath, newPath);
                        successCount++;
                    }
                }
                else if (request.Action == "enable")
                {
                    if (Path.GetFileName(fileName).EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase))
                    {
                        string newName = Path.GetFileName(fileName).Substring(0, Path.GetFileName(fileName).Length - ".disabled".Length);
                        string newPath = Path.Combine(targetPath, newName);

                        if (System.IO.File.Exists(newPath)) System.IO.File.Delete(newPath);

                        System.IO.File.Move(currentFilePath, newPath);
                        successCount++;
                    }
                }
                else if (request.Action == "delete")
                {
                    if (Path.GetFileName(fileName).EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(fileName).EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            System.IO.File.Delete(currentFilePath);
                            successCount++;
                        }
                        catch
                        {
                            failCount++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"处理文件 {Path.GetFileName(fileName)} 失败: {ex.Message}");
                failCount++;
            }
        }

        return Ok(new ApiResponse<object>
        {
            Code = 200,
            Message = $"操作完成。成功: {successCount}, 失败/忽略: {failCount}",
            Data = new { successCount, failCount }
        });
    }
}