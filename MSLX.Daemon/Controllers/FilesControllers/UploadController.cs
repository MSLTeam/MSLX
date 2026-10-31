using System.IO.Compression;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.Daemon.Services.DeployServerService;
using MSLX.SDK.Models;
using MSLX.SDK.Models.Files;
using Newtonsoft.Json.Linq;

namespace MSLX.Daemon.Controllers.FilesControllers;

[ApiController]
[Route("api/files/upload")]
public class UploadController : ControllerBase
{
    private readonly string _tempPath;
    private readonly IMemoryCache _memoryCache;
    private readonly ServerDeploymentService _deploymentService;

    public UploadController(IMemoryCache memoryCache, ServerDeploymentService deploymentService)
    {
        _memoryCache = memoryCache;
        _deploymentService = deploymentService;
        // 临时文件存放目录
        _tempPath = Path.Combine(IConfigBase.GetAppDataPath(), "Temp", "Uploads");
        if (!Directory.Exists(_tempPath)) Directory.CreateDirectory(_tempPath);
    }

    /// <summary>
    /// 获取上传key
    /// </summary>
    [HttpPost("init")]
    public IActionResult InitUpload()
    {
        var uploadId = Guid.NewGuid().ToString("N");

        // 1h有效
        _memoryCache.Set($"Upload_Session_{uploadId}", true, TimeSpan.FromHours(1));

        return Ok(new ApiResponse<JObject>
        {
            Code = 200,
            Message = "获取成功",
            Data = new JObject()
            {
                ["uploadId"] = uploadId
            }
        });
    }

    /// <summary>
    /// 上传分片
    /// </summary>
    [HttpPost("chunk/{uploadId}")]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> UploadChunk(string uploadId, [FromForm] int index, [FromForm] IFormFile file)
    {
        // 检查凭证
        if (!_memoryCache.TryGetValue($"Upload_Session_{uploadId}", out _))
        {
            return BadRequest(new ApiResponse<string> { Code = 403, Message = "上传凭证无效或已过期，请刷新页面重试" });
        }

        // 校验
        if (!Regex.IsMatch(uploadId, "^[a-fA-F0-9]{32}$"))
        {
            return BadRequest(new ApiResponse<string> { Code = 400, Message = "非法 ID" });
        }

        if (index < 0 || index > 1_000_000)
        {
            return BadRequest(new ApiResponse<string> { Code = 400, Message = "分片索引超出范围" });
        }

        if (file == null || file.Length == 0) return BadRequest("文件为空");

        // 暂存为 uploadId_index
        var chunkPath = Path.Combine(_tempPath, $"{uploadId}_{index}");
        var tempChunkPath = chunkPath + ".uploading";

        await using (var stream = new FileStream(tempChunkPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await file.CopyToAsync(stream);
        }

        // 使用原子替换，避免状态接口把半写入文件当成已完成分片。
        System.IO.File.Move(tempChunkPath, chunkPath, true);

        return Ok(new ApiResponse<JObject>
        {
            Code = 200,
            Message = "上传完成",
            Data = new JObject
            {
                ["index"] = index,
                ["size"] = file.Length,
            },
        });
    }

    /// <summary>
    /// 查询已接收的分片，用于网络中断后的恢复。
    /// </summary>
    [HttpGet("status/{uploadId}")]
    public IActionResult UploadStatus(string uploadId)
    {
        if (!Regex.IsMatch(uploadId ?? string.Empty, "^[a-fA-F0-9]{32}$"))
        {
            return BadRequest(new ApiResponse<string> { Code = 400, Message = "非法 ID" });
        }

        if (!_memoryCache.TryGetValue($"Upload_Session_{uploadId}", out _))
        {
            return BadRequest(new ApiResponse<string> { Code = 403, Message = "上传凭证无效或已过期，请刷新页面重试" });
        }

        var receivedChunks = GetChunkIndices(uploadId);
        return Ok(new ApiResponse<JObject>
        {
            Code = 200,
            Message = "查询成功",
            Data = new JObject
            {
                ["uploadId"] = uploadId,
                ["receivedChunks"] = new JArray(receivedChunks),
                ["receivedCount"] = receivedChunks.Count,
                ["status"] = "uploading",
            },
        });
    }

    /// <summary>
    /// 合并分片
    /// </summary>
    [HttpPost("finish/{uploadId}")]
    public async Task<IActionResult> MergeChunks(string uploadId, [FromBody] FileUploadMergeRequest req)
    {
        // 验证key
        if (!_memoryCache.TryGetValue($"Upload_Session_{uploadId}", out _))
        {
            return BadRequest(new ApiResponse<string> { Code = 403, Message = "凭证无效或已过期" });
        }

        if (req == null || req.TotalChunks <= 0)
        {
            return BadRequest(new ApiResponse<string> { Code = 400, Message = "分片总数必须大于 0" });
        }

        var finalPath = Path.Combine(_tempPath, uploadId + ".tmp");

        // 响应在网络中丢失时，客户端会重试 finish；已合并文件可以安全地重复确认。
        if (System.IO.File.Exists(finalPath))
        {
            return Ok(new ApiResponse<JObject>
            {
                Code = 200,
                Message = "文件上传全部成功",
                Data = new JObject { ["uploadId"] = uploadId },
            });
        }

        // 获取所有分片文件
        var chunksWithIndex = GetChunkFiles(uploadId);
        var chunks = chunksWithIndex.OrderBy(item => item.Index).Select(item => item.Path).ToList();

        var receivedIndices = chunksWithIndex.Select(item => item.Index).ToHashSet();
        var missingIndices = Enumerable.Range(0, req.TotalChunks).Where(index => !receivedIndices.Contains(index)).ToList();
        if (chunks.Count != req.TotalChunks || receivedIndices.Any(index => index < 0 || index >= req.TotalChunks))
        {
            return BadRequest(new ApiResponse<JObject>
            {
                Code = 400,
                Message = $"分片集合不完整，期望 {req.TotalChunks} 个，实际找到 {chunks.Count} 个",
                Data = new JObject
                {
                    ["receivedChunks"] = new JArray(receivedIndices.OrderBy(index => index)),
                    ["missingChunks"] = new JArray(missingIndices),
                },
            });
        }

        try
        {
            await using var destStream = new FileStream(finalPath, FileMode.Create);
            foreach (var chunkFile in chunks)
            {
                await using var sourceStream = new FileStream(chunkFile, FileMode.Open);
                await sourceStream.CopyToAsync(destStream);
            }
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiResponse<JObject>
            {
                Code = 500,
                Message = $"合并失败: {ex.Message}"
            });
        }
        // 合并成功后再清理分片；合并失败时保留它们，客户端可以查询状态后继续。
        // 清理分片
        foreach (var chunk in chunks)
        {
            try
            {
                System.IO.File.Delete(chunk);
            }
            catch
            {
            }
        }

        return Ok(new ApiResponse<JObject>
        {
            Code = 200,
            Message = "文件上传全部成功",
            Data = new JObject()
            {
                ["uploadId"] = uploadId
            }
        });
    }

    private List<int> GetChunkIndices(string uploadId)
    {
        return GetChunkFiles(uploadId)
            .Select(item => item.Index)
            .Distinct()
            .OrderBy(index => index)
            .ToList();
    }

    private List<(int Index, string Path)> GetChunkFiles(string uploadId)
    {
        var prefix = uploadId + "_";
        return Directory.EnumerateFiles(_tempPath, prefix + "*", SearchOption.TopDirectoryOnly)
            .Select(path =>
            {
                // 解析文件名中的 index
                var fileName = Path.GetFileName(path);
                if (!fileName.StartsWith(prefix, StringComparison.Ordinal) ||
                    !int.TryParse(fileName[prefix.Length..], out var index))
                {
                    return (Index: -1, Path: path);
                }

                return (Index: index, Path: path);
            })
            .Where(item => item.Index >= 0)
            .ToList();
    }

    /// <summary>
    /// 删除临时文件
    /// </summary>
    [HttpPost("delete/{uploadId}")]
    public IActionResult DeleteUpload(string uploadId)
    {
        // 简单的格式安全校验
        if (string.IsNullOrEmpty(uploadId) || !Regex.IsMatch(uploadId, "^[a-fA-F0-9]{32}$"))
        {
            return BadRequest(new ApiResponse<string> { Code = 400, Message = "ID 格式错误" });
        }

        // 从缓存移除
        _memoryCache.Remove($"Upload_Session_{uploadId}");

        var mergedPath = Path.Combine(_tempPath, uploadId + ".tmp");

        try
        {
            // 删除合并后的文件
            if (System.IO.File.Exists(mergedPath))
            {
                System.IO.File.Delete(mergedPath);
            }

            // 清理可能残留的分片
            var chunks = Directory.GetFiles(_tempPath, $"{uploadId}_*");
            foreach (var chunk in chunks)
            {
                try
                {
                    System.IO.File.Delete(chunk);
                }
                catch
                {
                }
            }

            return Ok(new ApiResponse<string> { Code = 200, Message = "清理成功" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiResponse<string> { Code = 500, Message = $"删除失败: {ex.Message}" });
        }
    }

    // 检查压缩包里面的jar文件 给整合包模式用的
    [HttpGet("inspect/{uploadId?}")]
    [Authorize(Roles = "admin")]
    public IActionResult InspectArchive(string? uploadId, [FromQuery] string? localPath = null)
    {
        string finalPath;
        if (!string.IsNullOrWhiteSpace(localPath))
        {
            // 本机路径
            finalPath = localPath;
        }
        else
        {
            // 上传的整合包
            if (string.IsNullOrEmpty(uploadId) || !Regex.IsMatch(uploadId, "^[a-fA-F0-9]{32}$"))
            {
                return BadRequest(new ApiResponse<string> { Code = 400, Message = "ID 格式错误" });
            }
            finalPath = Path.Combine(_tempPath, uploadId + ".tmp");
        }

        if (!System.IO.File.Exists(finalPath))
        {
            return NotFound(new ApiResponse<string> { Code = 404, Message = "找不到指定的压缩包文件，请检查路径或文件是否已过期" });
        }

        try
        {
            using var archive = ZipFile.Open(finalPath, ZipArchiveMode.Read, System.Text.Encoding.GetEncoding("GBK"));

            var mrpackData = _deploymentService.InspectMrpackArchive(archive);
            if (mrpackData != null)
            {
                return Ok(new ApiResponse<JObject>
                {
                    Code = 200,
                    Message = "解析成功",
                    Data = mrpackData
                });
            }
            
            // 获取所有 Entry 的顶层路径片段
            var topLevelSegments = archive.Entries
                .Where(e => !string.IsNullOrEmpty(e.FullName))
                .Select(e => e.FullName.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
                .Where(s => s != null)
                .Distinct()
                .ToList();

            string basePrefix = "";

            // 如果顶层只有一个片段 那就可能是套了个文件夹
            if (topLevelSegments.Count == 1)
            {
                var potentialRoot = topLevelSegments[0];

                // 检查这是否真的是个目录
                bool isFolderWrapper = archive.Entries.Any(e =>
                    e.FullName.StartsWith(potentialRoot + "/", StringComparison.OrdinalIgnoreCase));

                if (isFolderWrapper)
                {
                    basePrefix = potentialRoot + "/";
                }
            }

            // 扫描普通的单jar文件核心
            var jarFiles = archive.Entries
                // 处理Windows下的路径符号
                .Select(e => new { 
                    Entry = e, 
                    NormalizedName = e.FullName.Replace('\\', '/') 
                })
                .Where(x =>
                        !x.NormalizedName.EndsWith("/") && // 排除目录本身
                        x.NormalizedName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) // 只要 jar
                )
                .Select(x =>
                {
                    var name = x.NormalizedName;
                    // 如果有 basePrefix 只处理在这个目录下的文件
                    if (!string.IsNullOrEmpty(basePrefix))
                    {
                        if (!name.StartsWith(basePrefix, StringComparison.OrdinalIgnoreCase))
                            return null; // 不在根目录下

                        // 获取相对路径
                        return name.Substring(basePrefix.Length);
                    }

                    return name;
                })
                .Where(name => !string.IsNullOrEmpty(name)) // 过滤掉不符合前缀的
                .Where(name => !name!.Contains("/")) 
                .ToList();

            // 纯 NeoForge / Forge 端扫描
            bool isWindows = PlatFormServices.GetOs() == "Windows";
            string targetArgFile = isWindows ? "win_args.txt" : "unix_args.txt";

            var loaderPaths = new[]
            {
                $"{basePrefix}libraries/net/neoforged/neoforge/",
                $"{basePrefix}libraries/net/minecraftforge/forge/"
            };

            foreach (var basePath in loaderPaths)
            {
                var versions = archive.Entries
                    .Where(e => e.FullName.StartsWith(basePath, StringComparison.OrdinalIgnoreCase) &&
                                e.FullName.Length > basePath.Length)
                    .Select(e =>
                    {
                        var relativePath = e.FullName.Substring(basePath.Length);
                        var parts = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                        return parts.Length > 0 ? parts[0] : null;
                    })
                    .Where(v => v != null)
                    .Distinct()
                    .ToList();

                foreach (var version in versions)
                {
                    string versionPrefix = $"{basePath}{version}/";

                    // 检查该版本目录下是否至少有一个 jar 文件，用以判断安装情况
                    bool hasJar = archive.Entries.Any(e =>
                        e.FullName.StartsWith(versionPrefix, StringComparison.OrdinalIgnoreCase) &&
                        e.FullName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) &&
                        !e.FullName.Substring(versionPrefix.Length).Contains("/")
                    );

                    if (hasJar)
                    {
                        var argEntry = archive.Entries.FirstOrDefault(e =>
                            e.FullName.Equals($"{versionPrefix}{targetArgFile}", StringComparison.OrdinalIgnoreCase));

                        if (argEntry != null)
                        {
                            // 获取相对路径
                            string relativePath = string.IsNullOrEmpty(basePrefix)
                                ? argEntry.FullName
                                : argEntry.FullName.Substring(basePrefix.Length);
                            
                            jarFiles.Add($"@{relativePath}");
                        }
                    }
                }
            }

            // 扫描整合包根目录中的启动脚本
            var scriptPriority = new[] { "start", "run", "launch", "server" };

            var scripts = archive.Entries
                .Select(e => new { NormalizedName = e.FullName.Replace('\\', '/') })
                .Where(x =>
                    !x.NormalizedName.EndsWith("/") &&
                    (string.IsNullOrEmpty(basePrefix)
                        ? !x.NormalizedName.Contains("/")
                        : x.NormalizedName.StartsWith(basePrefix, StringComparison.OrdinalIgnoreCase) &&
                          !x.NormalizedName.Substring(basePrefix.Length).Contains("/")))
                .Select(x => string.IsNullOrEmpty(basePrefix)
                    ? x.NormalizedName
                    : x.NormalizedName.Substring(basePrefix.Length))
                .Where(name =>
                    name.EndsWith(".sh", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
                .OrderBy(name =>
                {
                    var baseName = Path.GetFileNameWithoutExtension(name).ToLower();
                    var idx = Array.IndexOf(scriptPriority, baseName);
                    return idx >= 0 ? idx : scriptPriority.Length;
                })
                .ToList();

            var metadataEntry = archive.Entries.FirstOrDefault(e => 
                e.FullName.Equals($"{basePrefix}mslx-pack-metadata.json", StringComparison.OrdinalIgnoreCase));
            
            JObject metadata = null;
            if (metadataEntry != null)
            {
                using var stream = metadataEntry.Open();
                using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
                metadata = JObject.Parse(reader.ReadToEnd());
            }

            return Ok(new ApiResponse<JObject>
            {
                Code = 200,
                Message = "解析成功",
                Data = new JObject
                {
                    ["count"] = jarFiles.Count,
                    ["jars"] = JToken.FromObject(jarFiles),
                    ["scripts"] = JToken.FromObject(scripts),
                    ["detectedRoot"] = basePrefix,
                    ["metadata"] = metadata,
                    ["format"] = "zip"
                }
            });
        }
        catch (InvalidDataException)
        {
            return BadRequest(new ApiResponse<string> { Code = 400, Message = "文件不是有效的压缩包" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiResponse<string> { Code = 500, Message = $"读取压缩包失败: {ex.Message}" });
        }
    }
}
