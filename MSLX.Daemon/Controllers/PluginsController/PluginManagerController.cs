using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.Models;
using MSLX.SDK.Models.Plugins;

namespace MSLX.Daemon.Controllers.PluginsController;

[ApiController]
[Route("api/plugins")]
public class PluginManagerController : ControllerBase
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<PluginManagerController> _logger;
    private static readonly ParallelDownloader _downloader = new ParallelDownloader();

    public PluginManagerController(IMemoryCache cache, ILogger<PluginManagerController> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    #region 插件安装(下载)

    // 提交下载
    [HttpPost("install")]
    [Authorize(Roles = "admin")]
    public IActionResult StartInstallPlugin([FromBody] InstallPluginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DownloadUrl) || string.IsNullOrWhiteSpace(request.FileName))
        {
            return BadRequest(new ApiResponse<object> { Code = 400, Message = "下载地址和文件名不能为空" });
        }

        var safeFileName = Path.GetFileName(request.FileName);

        var pluginsPath = Path.Combine(IConfigBase.GetAppDataPath(), "Plugins");
        var filePath = Path.Combine(pluginsPath, safeFileName);

        if (System.IO.File.Exists(filePath) && !request.Overwrite)
        {
            return BadRequest(new ApiResponse<object> { Code = 400, Message = $"插件 {safeFileName} 已存在，请勿重复安装！" });
        }

        // 任务ID
        string taskId = Guid.NewGuid().ToString("N");
        string cacheKey = $"Task_PluginInstall_{taskId}";

        UpdateStatus(cacheKey, "pending", 0, "准备开始下载插件...");

        _ = Task.Run(() => PerformDownloadTask(request, safeFileName, filePath, cacheKey));

        return Ok(new ApiResponse<object>
        {
            Code = 200,
            Message = "插件下载任务已提交",
            Data = new { TaskId = taskId }
        });
    }

    // 查进度
    [HttpGet("task/install/{taskId}")]
    [Authorize(Roles = "admin")]
    public IActionResult GetInstallStatus(string taskId)
    {
        if (_cache.TryGetValue($"Task_PluginInstall_{taskId}", out TaskStatusResponse? status))
        {
            return Ok(new ApiResponse<TaskStatusResponse>
            {
                Code = 200,
                Data = status
            });
        }
        return NotFound(new ApiResponse<object> { Code = 404, Message = "任务不存在或已过期" });
    }

    private async Task PerformDownloadTask(InstallPluginRequest request, string fileName, string targetFilePath, string cacheKey)
    {
        try
        {
            UpdateStatus(cacheKey, "processing", 0, "正在连接下载源...");

            var pluginsDir = Path.GetDirectoryName(targetFilePath);
            if (!Directory.Exists(pluginsDir) && pluginsDir != null)
            {
                Directory.CreateDirectory(pluginsDir);
            }

            if (request.Overwrite && System.IO.File.Exists(targetFilePath))
            {
                System.IO.File.Delete(targetFilePath);
            }

            var result = await _downloader.DownloadFileAsync(
                url: request.DownloadUrl,
                savePath: targetFilePath,
                onProgress: (progress, speed) =>
                {
                    UpdateStatus(cacheKey, "processing", (int)progress, $"正在下载: {speed}");
                },
                progressIntervalMs: 500
            );

            if (result.Success)
            {
                _logger.LogInformation($"插件 {fileName} 下载成功");
                UpdateStatus(cacheKey, "success", 100, "下载完成，将在守护进程下次重启时生效");
            }
            else
            {
                _logger.LogError($"插件 {fileName} 下载失败: {result.ErrorMessage}");
                UpdateStatus(cacheKey, "error", 0, $"下载失败: {result.ErrorMessage}");

                if (System.IO.File.Exists(targetFilePath))
                {
                    System.IO.File.Delete(targetFilePath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"处理插件下载任务异常: {ex.Message}");
            UpdateStatus(cacheKey, "error", 0, $"系统异常: {ex.Message}");
        }
    }

    private void UpdateStatus(string key, string status, int progress, string msg)
    {
        _cache.Set(key, new TaskStatusResponse
        {
            Status = status,
            Progress = progress,
            Message = msg
        }, TimeSpan.FromMinutes(30)); // 缓存30分钟
    }

    #endregion
}