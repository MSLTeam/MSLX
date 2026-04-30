using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.Models;
using MSLX.SDK.Models.Files;

namespace MSLX.Daemon.Controllers.FilesControllers;

[ApiController]
[Route("api/files")]
public class OfflineDownloadController : ControllerBase
{
    private readonly IMemoryCache _cache;

    public OfflineDownloadController(IMemoryCache memoryCache)
    {
        _cache = memoryCache;
    }

    // 提交离线下载任务
    [HttpPost("instance/{id}/download")]
    public IActionResult StartDownload(uint id, [FromBody] OfflineDownloadRequest request)
    {
        // 用户权限验证
        if (!IConfigBase.UserList.HasResourcePermission(User?.FindFirst("UserId")?.Value ?? "", "server", (int)id))
            return NotFound(ApiResponseService.NotFound());

        var server = IConfigBase.ServerList.GetServer(id);
        if (server == null) return NotFound(new ApiResponse<object> { Code = 404, Message = "实例不存在" });

        if (string.IsNullOrWhiteSpace(request.Url))
            return BadRequest(new ApiResponse<object> { Code = 400, Message = "下载地址不能为空" });

        // 任务ID
        string taskId = Guid.NewGuid().ToString("N");
        string cacheKey = $"Task_Download_{taskId}";

        // 初始化状态
        UpdateStatus(cacheKey, "pending", 0, "任务已排队，准备开始下载...");

        // 下崽
        _ = Task.Run(() => PerformDownloadTask(id, request, cacheKey));

        return Ok(new ApiResponse<object>
        {
            Code = 200,
            Message = "离线下载任务已提交",
            Data = new { TaskId = taskId }
        });
    }

    // 查询下载进度
    [HttpGet("task/download/{taskId}")]
    public IActionResult GetDownloadStatus(string taskId)
    {
        if (_cache.TryGetValue($"Task_Download_{taskId}", out TaskStatusResponse? status))
        {
            return Ok(new ApiResponse<TaskStatusResponse>
            {
                Code = 200,
                Data = status
            });
        }

        return NotFound(new ApiResponse<object> { Code = 404, Message = "任务不存在或已过期" });
    }

    // 下崽
    private async Task PerformDownloadTask(uint instanceId, OfflineDownloadRequest request, string cacheKey)
    {
        try
        {
            var server = IConfigBase.ServerList.GetServer(instanceId);
            if (server == null) throw new Exception("实例不存在");

            UpdateStatus(cacheKey, "processing", 0, "正在解析下载地址...");

            string fileName = request.FileName;
            if (string.IsNullOrWhiteSpace(fileName))
            {
                try
                {
                    var uri = new Uri(request.Url);
                    fileName = Path.GetFileName(uri.LocalPath);
                }
                catch
                {
                }

                if (string.IsNullOrWhiteSpace(fileName))
                {
                    fileName = $"download_{DateTimeOffset.Now.ToUnixTimeSeconds()}.tmp";
                }
            }

            string relativeDir = request.Path ?? "";
            string targetRelativePath = Path.Combine(relativeDir, fileName);

            var checkTarget = FileUtils.GetSafePath(server.Base, targetRelativePath);
            if (!checkTarget.IsSafe) throw new Exception("非法的保存路径");

            string savePath = checkTarget.FullPath;

            var downloader = new ParallelDownloader(parallelCount: 8, maxSimultaneousFiles: 1);

            var (success, errorMessage) = await downloader.DownloadFileAsync(
                url: request.Url,
                savePath: savePath,
                onProgress: (progress, speed) =>
                {
                    UpdateStatus(cacheKey, "processing", (int)progress, $"下载中... 速度: {speed}");
                },
                progressIntervalMs: 1000
            );

            if (success)
            {
                UpdateStatus(cacheKey, "success", 100, "下载完成");
            }
            else
            {
                if (System.IO.File.Exists(savePath))
                {
                    try
                    {
                        System.IO.File.Delete(savePath);
                    }
                    catch
                    {
                    }
                }

                UpdateStatus(cacheKey, "error", 0, $"下载失败: {errorMessage}");
            }
        }
        catch (Exception ex)
        {
            UpdateStatus(cacheKey, "error", 0, $"任务执行异常: {ex.Message}");
        }
    }

    private void UpdateStatus(string key, string status, int progress, string msg)
    {
        _cache.Set(key, new TaskStatusResponse
        {
            Status = status,
            Progress = progress,
            Message = msg
        }, TimeSpan.FromMinutes(30));
    }
}