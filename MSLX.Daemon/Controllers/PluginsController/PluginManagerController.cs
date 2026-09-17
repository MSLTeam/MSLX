using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.Models;
using MSLX.SDK.Models.Plugins;
using MSLX.Daemon.Services;
using MSLX.Daemon.Services.PluginsService;

namespace MSLX.Daemon.Controllers.PluginsController;

[ApiController]
[Route("api/plugins")]
public class PluginManagerController : ControllerBase
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<PluginManagerController> _logger;
    private readonly PluginManager _pluginManager;
    private static readonly ParallelDownloader _downloader = new ParallelDownloader();

    public PluginManagerController(IMemoryCache cache, ILogger<PluginManagerController> logger, PluginManager pluginManager)
    {
        _cache = cache;
        _logger = logger;
        _pluginManager = pluginManager;
    }

    #region 插件本地上传与热重载

    [HttpPost("upload")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> UploadLocalPlugin([FromBody] UploadPluginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FileId))
        {
            return BadRequest(new ApiResponse<object> { Code = 400, Message = "文件 ID 不能为空" });
        }

        var tempPath = Path.Combine(IConfigBase.GetAppDataPath(), "Temp", "Uploads", request.FileId + ".tmp");
        if (!System.IO.File.Exists(tempPath))
        {
            return NotFound(new ApiResponse<object> { Code = 404, Message = "未找到已上传的临时文件，可能已过期" });
        }

        try
        {
            // 预解析插件元数据并验证合法性
            var metadata = _pluginManager.GetPluginMetadata(tempPath);
            if (metadata == null || string.IsNullOrWhiteSpace(metadata.Id))
            {
                try { System.IO.File.Delete(tempPath); } catch { }
                return BadRequest(new ApiResponse<object> { Code = 400, Message = "上传的文件不是有效的 MSLX 插件，无法读取插件元数据或未实现 IPlugin 接口" });
            }

            var pluginId = metadata.Id;
            var pluginsPath = Path.Combine(IConfigBase.GetAppDataPath(), "Plugins");
            if (!Directory.Exists(pluginsPath))
            {
                Directory.CreateDirectory(pluginsPath);
            }

            // 检测是否存在同 ID 插件
            string? existingDllPath = null;
            var loadedPlugin = _pluginManager.Plugins.FirstOrDefault(p =>
                p.Metadata.Id.Equals(pluginId, StringComparison.OrdinalIgnoreCase));
            if (loadedPlugin != null)
            {
                existingDllPath = loadedPlugin.DllPath;
            }
            else
            {
                // 在磁盘上查找
                var allDlls = Directory.GetFiles(pluginsPath, "*.dll");
                foreach (var dll in allDlls)
                {
                    var fileMeta = _pluginManager.GetPluginMetadata(dll);
                    if (fileMeta != null && fileMeta.Id.Equals(pluginId, StringComparison.OrdinalIgnoreCase))
                    {
                        existingDllPath = dll;
                        break;
                    }
                }
            }

            string targetDllPath;
            if (!string.IsNullOrEmpty(existingDllPath))
            {
                _logger.LogInformation($"[MSLX Plugin] 上传插件检测到已有同 ID 插件 [{pluginId}]，准备覆盖更新: {existingDllPath}");
                _pluginManager.UnloadPlugin(existingDllPath);

                await Task.Delay(500);

                if (System.IO.File.Exists(existingDllPath))
                {
                    try
                    {
                        System.IO.File.Delete(existingDllPath);
                    }
                    catch
                    {
                        string backupName = existingDllPath + ".old_" + Guid.NewGuid().ToString("N");
                        System.IO.File.Move(existingDllPath, backupName);
                    }
                }

                // 清理可能的 PDB 调试文件
                string pdbPath = Path.ChangeExtension(existingDllPath, ".pdb");
                if (System.IO.File.Exists(pdbPath))
                {
                    try { System.IO.File.Delete(pdbPath); } catch { }
                }

                targetDllPath = existingDllPath;
            }
            else
            {
                // 全新插件
                string safeFileName;
                if (!string.IsNullOrWhiteSpace(request.FileName) && request.FileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    safeFileName = Path.GetFileName(request.FileName);
                }
                else
                {
                    safeFileName = (pluginId.Split('-')
                        .Select(w => w.Equals("mslx", StringComparison.OrdinalIgnoreCase) ? "MSLX" : char.ToUpper(w[0]) + w.Substring(1))
                        .Aggregate((a, b) => a + "." + b)) + ".dll";
                }
                safeFileName = Regex.Replace(safeFileName, @"[^a-zA-Z0-9_\-\.]", "");
                targetDllPath = Path.Combine(pluginsPath, safeFileName);
            }

            // 移动至目标插件位置
            if (System.IO.File.Exists(targetDllPath))
            {
                System.IO.File.Delete(targetDllPath);
            }
            System.IO.File.Move(tempPath, targetDllPath);

            // 热加载
            bool loadResult = _pluginManager.LoadPlugin(targetDllPath);
            if (loadResult)
            {
                _logger.LogInformation($"[MSLX Plugin] 本地插件 [{metadata.Name}] v{metadata.Version} ({pluginId}) 上传并热加载成功！");
                return Ok(new ApiResponse<object>
                {
                    Code = 200,
                    Message = $"插件 [{metadata.Name}] v{metadata.Version} 上传并热重载成功！",
                    Data = new
                    {
                        id = metadata.Id,
                        name = metadata.Name,
                        version = metadata.Version
                    }
                });
            }
            else
            {
                _logger.LogWarning($"[MSLX Plugin] 插件 [{metadata.Name}] 文件已写入但热加载失败");
                return Ok(new ApiResponse<object>
                {
                    Code = 200,
                    Message = $"插件文件已写入，但在热加载时失败，请查看控制台日志",
                    Data = new
                    {
                        id = metadata.Id,
                        name = metadata.Name,
                        version = metadata.Version
                    }
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[MSLX Plugin] 处理本地插件上传异常: {ex.Message}");
            return StatusCode(500, new ApiResponse<object> { Code = 500, Message = $"处理插件上传失败: {ex.Message}" });
        }
        finally
        {
            if (System.IO.File.Exists(tempPath))
            {
                try { System.IO.File.Delete(tempPath); } catch { }
            }
        }
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
                
                try 
                {
                    bool isUpdate = targetFilePath.EndsWith(".new", StringComparison.OrdinalIgnoreCase);
                    string realDllPath = isUpdate ? targetFilePath.Substring(0, targetFilePath.Length - 4) : targetFilePath;
                    
                    if (isUpdate)
                    {
                        _pluginManager.UnloadPlugin(realDllPath);
                        
                        // 小小的延迟确保文件句柄释放
                        await Task.Delay(500);
                        
                        if (System.IO.File.Exists(realDllPath))
                        {
                            try {
                                System.IO.File.Delete(realDllPath);
                            } catch {
                                // 强制重命名
                                string backupName = realDllPath + ".old_" + Guid.NewGuid().ToString("N");
                                System.IO.File.Move(realDllPath, backupName);
                            }
                        }
                        
                        // 预防插件存在PDB调试文件
                        string pdbPath = Path.ChangeExtension(realDllPath, ".pdb");
                        if (System.IO.File.Exists(pdbPath))
                        {
                            try { System.IO.File.Delete(pdbPath); } catch { }
                        }
                        
                        System.IO.File.Move(targetFilePath, realDllPath);
                    }
                    else
                    {
                        _pluginManager.UnloadPlugin(realDllPath);
                    }
                    
                    bool loadResult = _pluginManager.LoadPlugin(realDllPath);
                    if (loadResult)
                    {
                        UpdateStatus(cacheKey, "success", 100, "下载完成，已自动热重载并生效！");
                    }
                    else
                    {
                        UpdateStatus(cacheKey, "error", 0, "下载完成，但热加载插件失败（请查看控制台日志）");
                    }
                }
                catch (Exception ex)
                {
                    UpdateStatus(cacheKey, "success", 100, $"下载完成，但在热加载时遇到问题: {ex.Message}，将在重启时生效");
                }
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