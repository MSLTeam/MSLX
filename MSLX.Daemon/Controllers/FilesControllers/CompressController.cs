using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using MSLX.Daemon.Services;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.Models;
using MSLX.SDK.Models.Files;

namespace MSLX.Daemon.Controllers.FilesControllers;

[ApiController]
[Route("api/files")]
public class CompressController : ControllerBase
{
    private readonly IMemoryCache _cache;
    private readonly BackgroundTaskManager _taskManager;
    private readonly ArchiveService _archiveService;

    private static readonly string[] ArchiveExtensions =
    [
        ".tar.gz", ".tar.xz", ".tar.bz2", ".tar.zst",
        ".tgz", ".txz", ".tbz2",
        ".zip", ".jar", ".tar", ".7z", ".rar", ".gz", ".xz", ".bz2"
    ];

    public CompressController(IMemoryCache memoryCache, BackgroundTaskManager taskManager, ArchiveService archiveService)
    {
        _cache = memoryCache;
        _taskManager = taskManager;
        _archiveService = archiveService;
    }

    #region 压缩

    // 提交压缩任务
    [HttpPost("instance/{id}/compress")]
    public IActionResult StartCompress(uint id, [FromBody] CompressRequest request)
    {
        if (!IConfigBase.UserList.HasResourcePermission(User?.FindFirst("UserId")?.Value ?? "", "server", (int)id))
            return NotFound(ApiResponseService.NotFound());

        var server = IConfigBase.ServerList.GetServer(id);
        if (server == null) return NotFound(new ApiResponse<object> { Code = 404, Message = "实例不存在" });

        var userId = User?.FindFirst("UserId")?.Value ?? "";
        var (task, ct) = _taskManager.CreateTask(userId, id, TaskType.Compress, $"压缩: {request.TargetName}", request.TargetName);
        string taskId = task.Id;

        // 向下兼容旧接口
        _cache.Set($"Task_Compress_{taskId}", new TaskStatusResponse { Status = "pending", Message = "准备开始..." }, TimeSpan.FromMinutes(30));

        _ = Task.Run(() => PerformCompressionTask(id, request, taskId, ct), ct);

        return Ok(new ApiResponse<object>
        {
            Code = 200,
            Message = "压缩任务已提交",
            Data = new { TaskId = taskId }
        });
    }

    // 查询进度
    [HttpGet("task/compress/{taskId}")]
    public IActionResult GetCompressStatus(string taskId)
    {
        if (_cache.TryGetValue($"Task_Compress_{taskId}", out TaskStatusResponse? status))
        {
            return Ok(new ApiResponse<TaskStatusResponse>
            {
                Code = 200,
                Data = status
            });
        }
        return NotFound(new ApiResponse<object> { Code = 404, Message = "任务不存在或已过期" });
    }

    // 压缩逻辑
    private async Task PerformCompressionTask(uint instanceId, CompressRequest request, string taskId, CancellationToken ct)
    {
        try
        {
            var server = IConfigBase.ServerList.GetServer(instanceId);
            if (server == null) throw new Exception("实例不存在");

            UpdateStatus2(taskId, $"Task_Compress_{taskId}", "processing", 0, "正在扫描文件...");

            // 确定目标压缩包路径
            string relativeDir = request.CurrentPath ?? "";
            string targetName = request.TargetName.Trim();
            if (!HasArchiveExtension(targetName))
            {
                targetName += ".zip";
            }

            // 安全检查目标路径
            var checkTarget = FileUtils.GetSafePath(server.Base, Path.Combine(relativeDir, targetName));
            if (!checkTarget.IsSafe) throw new Exception("非法目标路径");

            string targetFilePath = checkTarget.FullPath;

            // 递归收集所有要压缩的文件
            var filesToCompress = new Dictionary<string, string>(); // <绝对路径, 归档内相对路径>

            foreach (var itemRelativePath in request.Sources)
            {
                string fullRelativePath = Path.Combine(relativeDir, itemRelativePath);
                var checkSrc = FileUtils.GetSafePath(server.Base, fullRelativePath);

                if (!checkSrc.IsSafe) continue; // 跳过非法文件
                string sourcePath = checkSrc.FullPath;

                if (Directory.Exists(sourcePath))
                {
                    var allFiles = Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories);
                    string parentDir = Path.GetDirectoryName(sourcePath)!;
                    foreach (var file in allFiles)
                    {
                        string entryName = Path.GetRelativePath(parentDir, file);
                        filesToCompress[file] = entryName;
                    }
                }
                else if (System.IO.File.Exists(sourcePath))
                {
                    filesToCompress[sourcePath] = Path.GetFileName(sourcePath);
                }
            }

            if (filesToCompress.Count == 0) throw new Exception("没有找到有效的文件可压缩");

            // 委托给 ArchiveService 处理压缩
            await _archiveService.CompressAsync(
                filesToCompress,
                targetFilePath,
                request.Password,
                ct,
                (percent, msg) =>
                {
                    UpdateStatus2(taskId, $"Task_Compress_{taskId}", "processing", percent, msg);
                });

            // 完成
            UpdateStatus2(taskId, $"Task_Compress_{taskId}", "success", 100, "压缩完成");
        }
        catch (Exception ex)
        {
            UpdateStatus2(taskId, $"Task_Compress_{taskId}", "error", 0, $"压缩失败: {ex.Message}");
        }
    }

    #endregion

    #region 解压

    // 提交解压任务
    [HttpPost("instance/{id}/decompress")]
    public IActionResult StartDecompress(uint id, [FromBody] DecompressRequest request)
    {
        if (!IConfigBase.UserList.HasResourcePermission(User?.FindFirst("UserId")?.Value ?? "", "server", (int)id))
            return NotFound(ApiResponseService.NotFound());

        var server = IConfigBase.ServerList.GetServer(id);
        if (server == null) return NotFound(new ApiResponse<object> { Code = 404, Message = "实例不存在" });

        var userId = User?.FindFirst("UserId")?.Value ?? "";
        var (task, ct) = _taskManager.CreateTask(userId, id, TaskType.Decompress, $"解压: {request.FileName}", request.FileName);
        string taskId = task.Id;

        // 向下兼容
        _cache.Set($"Task_Decompress_{taskId}", new TaskStatusResponse { Status = "pending", Message = "正在准备解压..." }, TimeSpan.FromMinutes(30));

        _ = Task.Run(() => PerformDecompressionTask(id, request, taskId, ct), ct);

        return Ok(new ApiResponse<object>
        {
            Code = 200,
            Message = "解压任务已提交",
            Data = new { TaskId = taskId }
        });
    }

    // 查询进度
    [HttpGet("task/decompress/{taskId}")]
    public IActionResult GetDecompressStatus(string taskId)
    {
        if (_cache.TryGetValue($"Task_Decompress_{taskId}", out TaskStatusResponse? status))
        {
            return Ok(new ApiResponse<TaskStatusResponse>
            {
                Code = 200,
                Data = status
            });
        }
        return NotFound(new ApiResponse<object> { Code = 404, Message = "任务不存在或已过期" });
    }

    // 核心解压逻辑
    private async Task PerformDecompressionTask(uint instanceId, DecompressRequest request, string taskId, CancellationToken ct)
    {
        try
        {
            var server = IConfigBase.ServerList.GetServer(instanceId);
            if (server == null) throw new Exception("实例不存在");

            UpdateStatus2(taskId, $"Task_Decompress_{taskId}", "processing", 0, "正在分析文件...");

            // 绝对路径
            string relativeDir = request.CurrentPath ?? "";
            string zipRelativePath = Path.Combine(relativeDir, request.FileName);

            var checkZip = FileUtils.GetSafePath(server.Base, zipRelativePath);
            if (!checkZip.IsSafe || !System.IO.File.Exists(checkZip.FullPath))
                throw new Exception("压缩包文件不存在或路径非法");

            string zipFullPath = checkZip.FullPath;

            // 确定解压的目标根目录
            string extractRootPath = Path.GetDirectoryName(zipFullPath)!;

            if (request.CreateSubFolder)
            {
                // 如果要求解压到子文件夹，正确剥离复合后缀并创建同名文件夹
                string folderName = StripArchiveExtension(Path.GetFileName(zipFullPath));
                extractRootPath = Path.Combine(extractRootPath, folderName);

                // 再次安全检查
                if (!FileUtils.GetSafePath(server.Base, Path.GetRelativePath(server.Base, extractRootPath)).IsSafe)
                    throw new Exception("生成的子目录路径非法");

                if (!Directory.Exists(extractRootPath))
                {
                    Directory.CreateDirectory(extractRootPath);
                }
            }

            // 委托给 ArchiveService 处理解压
            await _archiveService.DecompressAsync(
                zipFullPath,
                extractRootPath,
                request.Encoding,
                request.Password,
                ct,
                onProgress: (percent, msg) =>
                {
                    int reportedPercent = percent >= 0 ? percent : 50;
                    UpdateStatus2(taskId, $"Task_Decompress_{taskId}", "processing", reportedPercent, msg);
                },
                isPathSafe: dest => FileUtils.GetSafePath(server.Base, Path.GetRelativePath(server.Base, dest)).IsSafe
            );

            UpdateStatus2(taskId, $"Task_Decompress_{taskId}", "success", 100, "解压完成");
        }
        catch (Exception ex)
        {
            string errorMsg = ex.Message.StartsWith("解压失败") ? ex.Message : $"解压失败: {ex.Message}";
            UpdateStatus2(taskId, $"Task_Decompress_{taskId}", "error", 0, errorMsg);
        }
    }

    #endregion

    #region 状态与辅助

    private void UpdateStatus(string key, string status, int progress, string msg)
    {
        _cache.Set(key, new TaskStatusResponse
        {
            Status = status,
            Progress = progress,
            Message = msg
        }, TimeSpan.FromMinutes(30));
    }

    private void UpdateStatus2(string taskId, string cacheKey, string status, int progress, string msg)
    {
        UpdateStatus(cacheKey, status, progress, msg);
        if (status == "success") _taskManager.SetSuccess(taskId, msg);
        else if (status == "error") _taskManager.SetFailed(taskId, msg);
        else _taskManager.UpdateProgress(taskId, progress, msg, TaskState.Running);
    }

    private static bool HasArchiveExtension(string filename)
    {
        string lower = filename.ToLowerInvariant();
        return ArchiveExtensions.Any(ext => lower.EndsWith(ext));
    }

    private static string StripArchiveExtension(string filename)
    {
        string lower = filename.ToLowerInvariant();
        foreach (var ext in ArchiveExtensions)
        {
            if (lower.EndsWith(ext))
            {
                return filename.Substring(0, filename.Length - ext.Length);
            }
        }
        return Path.GetFileNameWithoutExtension(filename);
    }

    #endregion
}