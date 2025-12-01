using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory; 
using MSLX.Daemon.Models;
using MSLX.Daemon.Utils;
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
        _tempPath = Path.Combine(ConfigServices.GetAppDataPath(), "DaemonData", "Temp", "Uploads");
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
    public async Task<IActionResult> MergeChunks(string uploadId, [FromBody] MergeRequest req)
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

    public class MergeRequest
    {
        public int TotalChunks { get; set; }
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
}