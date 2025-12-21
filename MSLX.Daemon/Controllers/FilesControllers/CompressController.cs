using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using MSLX.Daemon.Models;
using MSLX.Daemon.Models.Files;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;

namespace MSLX.Daemon.Controllers.FilesControllers;

[ApiController]
[Route("api/files")]
public class CompressController: ControllerBase
{
    private readonly IMemoryCache _cache;
    
    public CompressController(IMemoryCache memoryCache)
    {
        _cache = memoryCache;
    }

    #region 压缩

    // 提交压缩任务
    [HttpPost("instance/{id}/compress")]
    public IActionResult StartCompress(uint id, [FromBody] CompressRequest request)
    {
        var server = IConfigBase.ServerList.GetServer(id);
        if (server == null) return NotFound(new ApiResponse<object> { Code = 404, Message = "实例不存在" });

        // 生成唯一任务ID
        string taskId = Guid.NewGuid().ToString("N");
        string cacheKey = $"Task_Compress_{taskId}";

        // 初始化状态
        _cache.Set(cacheKey, new TaskStatusResponse { Status = "pending", Message = "准备开始..." }, TimeSpan.FromMinutes(30));

        // 启动后台线程
        _ = Task.Run(() => PerformCompressionTask(id, request, cacheKey));
        
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
    private void PerformCompressionTask(uint instanceId, CompressRequest request, string cacheKey)
    {
        try
        {
            var server = IConfigBase.ServerList.GetServer(instanceId);
            if(server == null) throw new Exception("实例不存在");
            // 更新状态
            UpdateStatus(cacheKey, "processing", 0, "正在扫描文件...");

            // 确定目标压缩包路径
            string relativeDir = request.CurrentPath ?? "";
            string targetZipName = request.TargetName.EndsWith(".zip") ? request.TargetName : request.TargetName + ".zip";
            
            // 安全检查目标路径
            var checkTarget = FileUtils.GetSafePath(server.Base, Path.Combine(relativeDir, targetZipName));
            if (!checkTarget.IsSafe) throw new Exception("非法目标路径");
            
            string zipFilePath = checkTarget.FullPath;
            if (System.IO.File.Exists(zipFilePath)) System.IO.File.Delete(zipFilePath); // 覆盖旧的

            // 递归收集所有要压缩的文件
            var filesToCompress = new Dictionary<string, string>(); // <绝对路径, Zip内相对路径>
            
            foreach (var itemRelativePath in request.Sources)
            {
                // 拼接完整路径
                string fullRelativePath = Path.Combine(relativeDir, itemRelativePath);
                var checkSrc = FileUtils.GetSafePath(server.Base, fullRelativePath);
                
                if (!checkSrc.IsSafe) continue; // 跳过非法文件
                string sourcePath = checkSrc.FullPath;

                if (Directory.Exists(sourcePath))
                {
                    // 是文件夹：递归添加
                    var allFiles = Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories);
                    foreach (var file in allFiles)
                    {
                        string parentDir = Path.GetDirectoryName(sourcePath)!;
                        string entryName = Path.GetRelativePath(parentDir, file);
                        filesToCompress[file] = entryName;
                    }
                }
                else if (System.IO.File.Exists(sourcePath))
                {
                    // 是文件：直接添加
                    filesToCompress[sourcePath] = Path.GetFileName(sourcePath);
                }
            }

            if (filesToCompress.Count == 0) throw new Exception("没有找到有效的文件可压缩");

            // 开始压缩
            int total = filesToCompress.Count;
            int current = 0;

            // 使用 UTF-8 编码
            using (var zipToOpen = new FileStream(zipFilePath, FileMode.Create))
            using (var archive = new System.IO.Compression.ZipArchive(zipToOpen, System.IO.Compression.ZipArchiveMode.Create, true, System.Text.Encoding.UTF8))
            {
                foreach (var kvp in filesToCompress)
                {
                    string filePath = kvp.Key;
                    string entryName = kvp.Value;

                    // 更新进度
                    current++;
                    if (current % 5 == 0 || current == total)
                    {
                        int percent = (int)((double)current / total * 100);
                        UpdateStatus(cacheKey, "processing", percent, $"正在压缩: {entryName}");
                    }

                    // 写入 Zip Entry
                    archive.CreateEntryFromFile(filePath, entryName);
                }
            }

            // 完成
            UpdateStatus(cacheKey, "success", 100, "压缩完成");
        }
        catch (Exception ex)
        {
            UpdateStatus(cacheKey, "error", 0, $"压缩失败: {ex.Message}");
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

    #endregion

    #region 解压

    // 提交解压任务
    [HttpPost("instance/{id}/decompress")]
    public IActionResult StartDecompress(uint id, [FromBody] DecompressRequest request)
    {
        var server = IConfigBase.ServerList.GetServer(id);
        if (server == null) return NotFound(new ApiResponse<object> { Code = 404, Message = "实例不存在" });

        // 生成任务ID
        string taskId = Guid.NewGuid().ToString("N");
        string cacheKey = $"Task_Decompress_{taskId}";

        // 初始化状态
        _cache.Set(cacheKey, new TaskStatusResponse { Status = "pending", Message = "正在准备解压..." }, TimeSpan.FromMinutes(30));

        // 启动后台线程
        _ = Task.Run(() => PerformDecompressionTask(id, request, cacheKey));

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
    private void PerformDecompressionTask(uint instanceId, DecompressRequest request, string cacheKey)
    {
        try
        {
            var server = IConfigBase.ServerList.GetServer(instanceId);
            if(server == null) throw new Exception("实例不存在");

            UpdateStatus(cacheKey, "processing", 0, "正在分析文件...");

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
                // 如果要求解压到子文件夹，则创建一个同名文件夹
                string folderName = Path.GetFileNameWithoutExtension(zipFullPath);
                extractRootPath = Path.Combine(extractRootPath, folderName);
                
                // 再次安全检查
                if (!FileUtils.GetSafePath(server.Base, Path.GetRelativePath(server.Base, extractRootPath)).IsSafe)
                    throw new Exception("生成的子目录路径非法");

                if (!Directory.Exists(extractRootPath))
                {
                    Directory.CreateDirectory(extractRootPath);
                }
            }

            // 确定编码
            Encoding encoding = GetEncoding(request.Encoding, zipFullPath);
            
            // 开始解压
            using (var fs = System.IO.File.OpenRead(zipFullPath))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false, entryNameEncoding: encoding))
            {
                int total = archive.Entries.Count;
                int current = 0;

                foreach (var entry in archive.Entries)
                {
                    current++;
                    
                    // 进度防抖
                    if (current % 10 == 0 || current == total)
                    {
                        int percent = (int)((double)current / total * 100);
                        UpdateStatus(cacheKey, "processing", percent, $"正在解压: {entry.Name}");
                    }

                    // Zip Slip 防御：跳过包含 ".." 的恶意路径
                    if (entry.FullName.Contains("..") || entry.FullName.Contains(":\\"))
                    {
                        continue; 
                    }

                    // 组合最终目标路径 = 解压根目录 + Zip内路径
                    string destinationPath = Path.Combine(extractRootPath, entry.FullName);
                    
                    // 最终安全检查
                    if (!FileUtils.GetSafePath(server.Base, Path.GetRelativePath(server.Base, destinationPath)).IsSafe)
                    {
                        continue;
                    }

                    // 处理逻辑
                    if (string.IsNullOrEmpty(entry.Name) || entry.FullName.EndsWith("/"))
                    {
                        // 是目录
                        if (!Directory.Exists(destinationPath)) Directory.CreateDirectory(destinationPath);
                    }
                    else
                    {
                        // 是文件
                        // 确保父文件夹存在
                        string? parentDir = Path.GetDirectoryName(destinationPath);
                        if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                        {
                            Directory.CreateDirectory(parentDir);
                        }

                        // 写入文件 (覆盖旧文件)
                        entry.ExtractToFile(destinationPath, overwrite: true);
                    }
                }
            }

            UpdateStatus(cacheKey, "success", 100, "解压完成");
        }
        catch (Exception ex)
        {
            UpdateStatus(cacheKey, "error", 0, $"解压失败: {ex.Message}");
        }
    }

    // 获取编码
    private Encoding GetEncoding(string userChoice, string zipPath)
    {
        // 用户强制指定
        if (!string.IsNullOrEmpty(userChoice) && userChoice.ToLower() != "auto")
        {
            try 
            {
                return Encoding.GetEncoding(userChoice);
            }
            catch 
            {
                // 忽略错误，回退到 Auto
            }
        }

        // 自动检测逻辑
        var utf8 = Encoding.UTF8;
        try
        {
            using (var fs = System.IO.File.OpenRead(zipPath))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: true, entryNameEncoding: utf8))
            {
                foreach (var entry in archive.Entries)
                {
                    // 检查乱码特征
                    if (entry.FullName.Contains('\uFFFD')) 
                    {
                        return Encoding.GetEncoding("GBK");
                    }
                }
            }
            return utf8;
        }
        catch
        {
            return Encoding.GetEncoding("GBK");
        }
    }
    #endregion
}