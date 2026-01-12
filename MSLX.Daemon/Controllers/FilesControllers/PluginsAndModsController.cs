using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models;
using MSLX.Daemon.Models.Files;
using MSLX.Daemon.Utils.ConfigUtils;
using System.IO.Compression; 
using System.Text.Json;  
using System.Text.RegularExpressions;

namespace MSLX.Daemon.Controllers.FilesControllers;

[ApiController]
[Route("api/files/pm")]
public class PluginsAndModsController : ControllerBase
{
    [HttpGet("instance/{id}/list")]
    public IActionResult GetPluginsAndModsList(uint id, [FromQuery] string? mode = "plugins", [FromQuery] bool checkClient = false)
    {
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

            string targetPath = mode == "plugins" ? Path.Combine(server.Base, "plugins") : Path.Combine(server.Base, "mods");
            if (!Directory.Exists(targetPath))
            {
                return NotFound(new ApiResponse<object>
                {
                    Code = 404,
                    Message = $"未检测到{(mode == "plugins" ? "插件" : "模组")}目录,请检查当前服务端是否支持使用{(mode == "plugins" ? "插件" : "模组")}，或者尝试启动一次服务器。",
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
                // 倒序遍历以便在循环中安全移除元素
                for (int i = jarFilesList.Count - 1; i >= 0; i--)
                {
                    string? fileName = jarFilesList[i];
                    if (fileName == null) continue;

                    string fullPath = Path.Combine(targetPath, fileName);
                    
                    // 调用检测方法
                    if (IsClientSideMod(fullPath))
                    {
                        // 是客户端模组：添加到客户端列表，并从原列表移除
                        clientJarFiles.Add(fileName);
                        jarFilesList.RemoveAt(i);
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

    /// <summary>
    /// 检测 jar 文件是否为仅客户端模组
    /// </summary>
    private bool IsClientSideMod(string filePath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(filePath);

            //  Fabric (fabric.mod.json) 
            var fabricEntry = archive.GetEntry("fabric.mod.json");
            if (fabricEntry != null)
            {
                using var stream = fabricEntry.Open();
                using var doc = JsonDocument.Parse(stream);
                if (doc.RootElement.TryGetProperty("environment", out var envElement))
                {
                    string? env = envElement.GetString();
                    if ("client".Equals(env, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            //  Forge/NeoForge (META-INF/mods.toml 或 META-INF/neoforge.mods.toml)
            var tomlEntry = archive.GetEntry("META-INF/mods.toml") ?? archive.GetEntry("META-INF/neoforge.mods.toml");

            if (tomlEntry != null)
            {
                using var stream = tomlEntry.Open();
                using var reader = new StreamReader(stream);
                string content = reader.ReadToEnd();

                // 正则表达式拆分内容
                var blocks = Regex.Matches(content, @"(?ms)^\[\[.*?\]\](.*?)(?=^\[\[|\z)");

                string? minecraftSide = null;
                string? firstFoundSide = null;

                foreach (Match block in blocks)
                {
                    string blockBody = block.Groups[1].Value;

                    // 在块内匹配 modId 和 side
                    // 必须匹配行首的 key，避免匹配到 description 或其他字符串中的内容
                    var modIdMatch = Regex.Match(blockBody, @"^\s*modId\s*=\s*[""'](.*?)[""']", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                    var sideMatch = Regex.Match(blockBody, @"^\s*side\s*=\s*[""'](.*?)[""']", RegexOptions.Multiline | RegexOptions.IgnoreCase);

                    if (sideMatch.Success)
                    {
                        string currentSide = sideMatch.Groups[1].Value;

                        // 记录遇到的第一个 side（回退逻辑）
                        if (firstFoundSide == null)
                        {
                            firstFoundSide = currentSide;
                        }

                        // 如果当前块的 modId 是 minecraft，则将其作为最高优先级并跳出
                        if (modIdMatch.Success && string.Equals(modIdMatch.Groups[1].Value, "minecraft", StringComparison.OrdinalIgnoreCase))
                        {
                            minecraftSide = currentSide;
                            break;
                        }
                    }
                }

                // 优先使用 minecraft 的 side，如果没有，则使用找到的第一个 side
                string? finalSide = minecraftSide ?? firstFoundSide;

                if (finalSide != null && string.Equals(finalSide, "CLIENT", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            // 如果文件损坏或无法读取，默认为非客户端模组
            return false;
        }
    }

    [HttpPost("instance/{id}/set")]
    public IActionResult SetPluginOrModState(uint id, [FromBody] SetPluginModStateRequest request)
    {
        var server = IConfigBase.ServerList.GetServer(id);
        if (server == null)
            return NotFound(new ApiResponse<object> { Code = 404, Message = "服务器不存在" });

        string targetPath = request.Mode == "plugins" ? Path.Combine(server.Base, "plugins") : Path.Combine(server.Base, "mods");
        if (!Directory.Exists(targetPath))
            return NotFound(new ApiResponse<object> { Code = 404, Message = "目录不存在" });

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