using System.IO.Compression;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory; 
using MSLX.Daemon.Models;
using MSLX.Daemon.Models.Files;
using MSLX.Daemon.Utils.ConfigUtils;
using Newtonsoft.Json.Linq;

namespace MSLX.Daemon.Controllers.FilesControllers;

[ApiController]
[Route("api/files/upload")]
public class UploadController : ControllerBase
{
    private readonly string _tempPath;
    private readonly IMemoryCache _memoryCache;
    
    public UploadController(IMemoryCache memoryCache)
    {
        _memoryCache = memoryCache;
        // 临时文件存放目录
        _tempPath = Path.Combine(IConfigBase.GetAppDataPath(), "DaemonData", "Temp", "Uploads");
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

        if (file == null || file.Length == 0) return BadRequest("文件为空");

        // 暂存为 uploadId_index
        var chunkPath = Path.Combine(_tempPath, $"{uploadId}_{index}");

        using (var stream = new FileStream(chunkPath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        return Ok(new ApiResponse<JObject>
        {
            Code = 200,
            Message = "上传完成",
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

        var finalPath = Path.Combine(_tempPath, uploadId + ".tmp");
        
        // 获取所有分片文件
        var chunks = Directory.GetFiles(_tempPath, $"{uploadId}_*")
            .OrderBy(f => {
                // 解析文件名中的 index
                var fileName = Path.GetFileName(f);
                var parts = fileName.Split('_');
                return parts.Length > 1 && int.TryParse(parts[1], out int idx) ? idx : 0;
            })
            .ToList();

        if (chunks.Count != req.TotalChunks)
        {
            return BadRequest(new ApiResponse<JObject>
            {
                Code = 400,
                Message = $"分片数量不匹配，期望 {req.TotalChunks}，实际找到 {chunks.Count}",
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
        finally
        {
            // 清理分片
            foreach (var chunk in chunks)
            {
                try { System.IO.File.Delete(chunk); } catch { }
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
                try { System.IO.File.Delete(chunk); } catch { }
            }

            return Ok(new ApiResponse<string> { Code = 200, Message = "清理成功" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiResponse<string> { Code = 500, Message = $"删除失败: {ex.Message}" });
        }
    }
    
    // 检查压缩包里面的jar文件 给整合包模式用的
    [HttpGet("inspect/{uploadId}")]
    public IActionResult InspectArchive(string uploadId)
    {
        // 基础校验
        if (string.IsNullOrEmpty(uploadId) || !Regex.IsMatch(uploadId, "^[a-fA-F0-9]{32}$"))
        {
            return BadRequest(new ApiResponse<string> { Code = 400, Message = "ID 格式错误" });
        }

        var finalPath = Path.Combine(_tempPath, uploadId + ".tmp");
        if (!System.IO.File.Exists(finalPath))
        {
            return NotFound(new ApiResponse<string> { Code = 404, Message = "找不到指定的上传文件，可能已过期" });
        }

        try
        {
            using var archive = ZipFile.OpenRead(finalPath);
            
            // 判定根目录逻辑
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
                
                // 检查这是否真的是个目录：
                // 逻辑：存在任意文件以 "potentialRoot/" 开头，且长度大于它（说明里面有东西）
                // 注意：ZipEntry 有时单独存目录项，有时不存，所以靠路径匹配最稳
                bool isFolderWrapper = archive.Entries.Any(e => 
                    e.FullName.StartsWith(potentialRoot + "/", StringComparison.OrdinalIgnoreCase));

                if (isFolderWrapper)
                {
                    basePrefix = potentialRoot + "/";
                }
            }

            // 扫描jar包
            var jarFiles = archive.Entries
                .Where(e => 
                    !e.FullName.EndsWith("/") && // 排除目录本身
                    e.FullName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) // 只要 jar
                )
                .Select(e =>
                {
                    // 如果有 basePrefix 只处理在这个目录下的文件
                    if (!string.IsNullOrEmpty(basePrefix))
                    {
                        if (!e.FullName.StartsWith(basePrefix, StringComparison.OrdinalIgnoreCase))
                            return null; // 不在根目录下
                        
                        // 获取相对路径
                        return e.FullName.Substring(basePrefix.Length); 
                    }
                    return e.FullName;
                })
                .Where(name => !string.IsNullOrEmpty(name)) // 过滤掉不符合前缀的
                .Where(name => !name!.Contains("/")) // 关键：不递归子目录
                .ToList();

            return Ok(new ApiResponse<JObject>
            {
                Code = 200,
                Message = "解析成功",
                Data = new JObject
                {
                    ["count"] = jarFiles.Count,
                    ["jars"] = JToken.FromObject(jarFiles),
                    ["detectedRoot"] = basePrefix
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