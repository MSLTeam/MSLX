using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.Models;
using MSLX.SDK.Models.Plugins;
using MSLX.Daemon.Services.PluginsService;

namespace MSLX.Daemon.Controllers.PluginsController;

[ApiController]
[Route("api/plugins")]
public class PluginManagerController : ControllerBase
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<PluginManagerController> _logger;
    private readonly PluginPackageService _packages;
    private static readonly ParallelDownloader _downloader = new ParallelDownloader();

    public PluginManagerController(IMemoryCache cache, ILogger<PluginManagerController> logger, PluginPackageService packages)
    {
        _cache = cache;
        _logger = logger;
        _packages = packages;
    }

    #region 插件本地上传与热重载

    [HttpPost("upload")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> UploadLocalPlugin([FromBody] UploadPluginRequest request)
    {
        if (!Guid.TryParseExact(request.FileId, "N", out _))
            return BadRequest(new ApiResponse<object> { Code = 400, Message = "文件 ID 无效" });
        var tempPath = Path.Combine(IConfigBase.GetAppDataPath(), "Temp", "Uploads", request.FileId + ".tmp");
        if (!System.IO.File.Exists(tempPath)) return NotFound(new ApiResponse<object> { Code = 404, Message = "未找到已上传的临时文件" });
        try
        {
            var result = await _packages.InstallAsync(tempPath, request, HttpContext.RequestAborted);
            return Ok(new ApiResponse<object> { Code = 200, Message = result.Enabled ? "插件已安装并加载" : "插件已安装，保持禁用状态", Data = result });
        }
        catch (SyncConflictException ex) { return Conflict(new ApiResponse<object> { Code = 409, Message = ex.Message }); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "插件安装失败");
            return BadRequest(new ApiResponse<object> { Code = 400, Message = ex.Message });
        }
        finally { if (System.IO.File.Exists(tempPath)) System.IO.File.Delete(tempPath); }
    }

    #endregion

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

        _ = Task.Run(() => PerformDownloadTask(request, safeFileName, cacheKey));

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

    private async Task PerformDownloadTask(InstallPluginRequest request, string fileName, string cacheKey)
    {
        var staging = Path.Combine(IConfigBase.GetAppDataPath(), "Temp", "PluginDownloads", Guid.NewGuid().ToString("N") + ".dll");
        Directory.CreateDirectory(Path.GetDirectoryName(staging)!);
        try
        {
            UpdateStatus(cacheKey, "processing", 0, "正在连接下载源...");
            var result = await _downloader.DownloadFileAsync(request.DownloadUrl, staging,
                onProgress: (progress, speed) => UpdateStatus(cacheKey, "processing", (int)progress, $"正在下载: {speed}"), progressIntervalMs: 500);
            if (!result.Success) throw new IOException("下载失败: " + result.ErrorMessage);
            var installed = await _packages.InstallAsync(staging, new UploadPluginRequest
            {
                FileName = fileName.EndsWith(".new", StringComparison.OrdinalIgnoreCase) ? fileName[..^4] : fileName,
                Overwrite = request.Overwrite
            }, CancellationToken.None);
            UpdateStatus(cacheKey, "success", 100, installed.Enabled ? "插件已安装并加载" : "插件已安装，保持禁用状态");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "插件下载或安装失败");
            UpdateStatus(cacheKey, "error", 0, ex.Message);
        }
        finally { if (System.IO.File.Exists(staging)) System.IO.File.Delete(staging); }
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