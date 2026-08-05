using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Services;
using MSLX.Daemon.Utils;
using System.Threading.Tasks;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.IServices;
using MSLX.SDK.Models.Instance;
using Microsoft.Extensions.Caching.Memory;
using System.IO.Compression;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;

namespace MSLX.Daemon.Controllers.InstanceControllers;

[Route("api/instance")]
[ApiController]
public class InstanceExportController : ControllerBase
{
    private readonly IMemoryCache _memoryCache;
    private readonly IJavaScannerService _javaScannerService;

    public InstanceExportController(IMemoryCache memoryCache, IJavaScannerService javaScannerService)
    {
        _memoryCache = memoryCache;
        _javaScannerService = javaScannerService;
    }

    [HttpPost("{id}/export")]
    public IActionResult ExportInstance(uint id, [FromBody] ExportInstanceRequest request)
    {
        var server = IConfigBase.ServerList.GetServer(id);
        if (server == null)
            return NotFound(ApiResponseService.NotFound("实例不存在"));

        if (!IConfigBase.UserList.HasResourcePermission(User?.FindFirst("UserId")?.Value ?? "", "server", (int)id))
            return NotFound(ApiResponseService.NotFound());

        var mslxPacksDir = Path.Combine(server.Base, "mslx-packs");
        if (!Directory.Exists(mslxPacksDir)) Directory.CreateDirectory(mslxPacksDir);

        var taskId = Guid.NewGuid().ToString("N");
        var timestamp = DateTime.Now.ToString("yyyy-MMdd-HHmm");
        var safeName = string.Join("_", server.Name.Split(Path.GetInvalidFileNameChars()));
        var exportPath = Path.Combine(mslxPacksDir, $"mslx-pack-{safeName}-{timestamp}.zip");
        
        _memoryCache.Set($"ExportTask_{taskId}", "Processing", TimeSpan.FromHours(1));

        Task.Run(() =>
        {
            try
            {
                using var archive = ZipFile.Open(exportPath, ZipArchiveMode.Create, System.Text.Encoding.GetEncoding("GBK"));
                var baseUri = new Uri(server.Base.TrimEnd('\\', '/') + "/");

                foreach (var file in Directory.GetFiles(server.Base, "*", SearchOption.AllDirectories))
                {
                    var relativePath = Uri.UnescapeDataString(baseUri.MakeRelativeUri(new Uri(file)).ToString());
                    
                    // Exclude mslx-packs folder
                    if (relativePath.StartsWith("mslx-packs/", StringComparison.OrdinalIgnoreCase))
                        continue;
                    
                    // Check if file is in any excluded directory
                    bool isExcluded = false;
                    if (request.Excludes != null)
                    {
                        foreach (var exclude in request.Excludes)
                        {
                            if (relativePath.StartsWith(exclude + "/", StringComparison.OrdinalIgnoreCase) || 
                                relativePath.Equals(exclude, StringComparison.OrdinalIgnoreCase))
                            {
                                isExcluded = true;
                                break;
                            }
                        }
                    }

                    if (!isExcluded)
                    {
                        archive.CreateEntryFromFile(file, relativePath);
                    }
                }

                // Add metadata
                var metaEntry = archive.CreateEntry("mslx-pack-metadata.json");
                using var stream = metaEntry.Open();
                using var writer = new StreamWriter(stream, System.Text.Encoding.UTF8);
                
                var serverInfoObj = JsonConvert.DeserializeObject<MSLX.SDK.Models.McServerInfo.ServerInfo>(JsonConvert.SerializeObject(server));
                
                string javaVerStr = "17"; // 默认值
                if (serverInfoObj.Java != null)
                {
                    // 尝试调用自带的Java扫描服务获取准确版本
                    if (!serverInfoObj.Java.StartsWith("docker:", StringComparison.OrdinalIgnoreCase) && !serverInfoObj.Java.StartsWith("MSLX://", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var javaList = _javaScannerService.ScanJavaAsync().GetAwaiter().GetResult();
                            var javaInfo = javaList.FirstOrDefault(j => j.Path.Equals(serverInfoObj.Java, StringComparison.OrdinalIgnoreCase));
                            if (javaInfo != null && !string.IsNullOrEmpty(javaInfo.Version))
                            {
                                var match = System.Text.RegularExpressions.Regex.Match(javaInfo.Version, @"\d+");
                                if (match.Success)
                                {
                                    javaVerStr = match.Value;
                                }
                            }
                        }
                        catch
                        {
                            // 忽略异常，继续用后备正则
                        }
                    }

                    // 如果仍不是纯数字，则使用正则提取（涵盖 docker 镜像情况）
                    if (!System.Text.RegularExpressions.Regex.IsMatch(javaVerStr, @"^\d+$") && javaVerStr == "17") // 还没命中
                    {
                        var match = System.Text.RegularExpressions.Regex.Match(serverInfoObj.Java, @"java\s*[-_]?\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            javaVerStr = match.Groups[1].Value;
                        }
                        else
                        {
                            match = System.Text.RegularExpressions.Regex.Match(serverInfoObj.Java, @"jdk\s*[-_]?\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            if (match.Success)
                            {
                                javaVerStr = match.Groups[1].Value;
                            }
                            else
                            {
                                match = System.Text.RegularExpressions.Regex.Match(serverInfoObj.Java, @"jre\s*[-_]?\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                if (match.Success)
                                {
                                    javaVerStr = match.Groups[1].Value;
                                }
                                else
                                {
                                    match = System.Text.RegularExpressions.Regex.Match(serverInfoObj.Java, @"\d+");
                                    if (match.Success)
                                    {
                                        javaVerStr = match.Value;
                                    }
                                }
                            }
                        }
                    }
                }

                var customMetadata = new
                {
                    version = 1,
                    config = new
                    {
                        java = javaVerStr,
                        core = serverInfoObj.Core,
                        args = serverInfoObj.Args,
                        minM = serverInfoObj.MinM,
                        maxM = serverInfoObj.MaxM
                    }
                };

                writer.Write(JsonConvert.SerializeObject(customMetadata, Formatting.Indented));
                
                _memoryCache.Set($"ExportTask_{taskId}", "Completed:" + exportPath, TimeSpan.FromHours(1));
            }
            catch (Exception ex)
            {
                _memoryCache.Set($"ExportTask_{taskId}", "Error:" + ex.Message, TimeSpan.FromHours(1));
            }
        });

        return Ok(ApiResponseService.Success(new { taskId = taskId }));
    }
}
