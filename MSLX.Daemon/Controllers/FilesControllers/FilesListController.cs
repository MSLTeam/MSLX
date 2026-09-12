using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.Models;
using MSLX.SDK.Models.Files;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace MSLX.Daemon.Controllers.FilesControllers;

[ApiController]
[Route("api/files")]
public class FilesListController : ControllerBase
{
    private static readonly FileExtensionContentTypeProvider _contentTypeProvider = new()
    {
        Mappings =
        {
            [".mkv"] = "video/x-matroska",
            [".flv"] = "video/x-flv"
        }
    };
    // 获取文件列表
    [HttpGet("instance/{id}/lists")]
    public IActionResult GetFilesList(
        uint id, 
        [FromQuery] string? path = "",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 0,
        [FromQuery] string? search = null,
        [FromQuery] string? sort = "name",
        [FromQuery] string? order = "asc")
    {
        if (!IConfigBase.UserList.HasResourcePermission(User?.FindFirst("UserId")?.Value ?? "", "server", (int)id))
            return NotFound(ApiResponseService.NotFound());
        
        var server = IConfigBase.ServerList.GetServer(id);
        if (server == null)
        {
            return NotFound(new ApiResponse<object>
            {
                Code = 404,
                Message = "找不到指定的服务端实例"
            });
        }

        if (string.IsNullOrEmpty(server.Base) || !Directory.Exists(server.Base))
        {
            return NotFound(new ApiResponse<object>
            {
                Code = 404,
                Message = "服务端根目录不存在或未配置"
            });
        }

        try
        {
            var check = FileUtils.GetSafePath(server.Base, path);
        
            if (!check.IsSafe)
            {
                return BadRequest(new ApiResponse<object> { Code = 403, Message = check.Message });
            }
        
            string targetPath = check.FullPath;

            if (!Directory.Exists(targetPath))
            {
                return NotFound(new ApiResponse<object> { Code = 404, Message = "目录不存在" });
            }

            var directoryInfo = new DirectoryInfo(targetPath);

            // 没有指定pagesize 那么默认走全量
            if (pageSize <= 0)
            {
                var resultList = new List<FileItem>();

                var dirs = directoryInfo.GetDirectories();
                foreach (var dir in dirs)
                {
                    resultList.Add(new FileItem
                    {
                        Name = dir.Name,
                        Type = "folder",
                        Size = 0, 
                        LastModified = dir.LastWriteTime,
                        Permission = GetUnixPermissionSafe(dir)
                    });
                }

                var files = directoryInfo.GetFiles();
                foreach (var file in files)
                {
                    resultList.Add(new FileItem
                    {
                        Name = file.Name,
                        Type = "file",
                        Size = file.Length,
                        LastModified = file.LastWriteTime,
                        Permission = GetUnixPermissionSafe(file)
                    });
                }

                var sortedList = resultList
                    .OrderByDescending(x => x.Type == "folder") 
                    .ThenBy(x => x.Name) 
                    .ToList();

                return Ok(new ApiResponse<List<FileItem>>
                {
                    Code = 200,
                    Message = "获取成功",
                    Data = sortedList
                });
            }

            // 分页流式处理逻辑
            IEnumerable<DirectoryInfo> dirQuery = directoryInfo.EnumerateDirectories();
            IEnumerable<FileInfo> fileQuery = directoryInfo.EnumerateFiles();

            if (!string.IsNullOrWhiteSpace(search))
            {
                dirQuery = dirQuery.Where(d => d.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
                fileQuery = fileQuery.Where(f => f.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
            }

            bool isDesc = string.Equals(order, "desc", StringComparison.OrdinalIgnoreCase);

            if (string.Equals(sort, "time", StringComparison.OrdinalIgnoreCase))
            {
                dirQuery = isDesc ? dirQuery.OrderBy(d => d.LastWriteTime) : dirQuery.OrderByDescending(d => d.LastWriteTime);
                fileQuery = isDesc ? fileQuery.OrderBy(f => f.LastWriteTime) : fileQuery.OrderByDescending(f => f.LastWriteTime);
            }
            else if (string.Equals(sort, "size", StringComparison.OrdinalIgnoreCase))
            {
                dirQuery = dirQuery.OrderBy(d => d.Name);
                fileQuery = isDesc ? fileQuery.OrderBy(f => f.Length) : fileQuery.OrderByDescending(f => f.Length);
            }
            else
            {
                dirQuery = isDesc ? dirQuery.OrderByDescending(d => d.Name) : dirQuery.OrderBy(d => d.Name);
                fileQuery = isDesc ? fileQuery.OrderByDescending(f => f.Name) : fileQuery.OrderBy(f => f.Name);
            }

            var dirList = dirQuery.ToList();
            var fileList = fileQuery.ToList();
            int totalCount = dirList.Count + fileList.Count;

            if (page < 1) page = 1;
            int skip = (page - 1) * pageSize;

            var pagedItems = new List<FileItem>();

            if (skip < dirList.Count)
            {
                var dirsToTake = dirList.Skip(skip).Take(pageSize).ToList();
                foreach (var dir in dirsToTake)
                {
                    pagedItems.Add(new FileItem
                    {
                        Name = dir.Name,
                        Type = "folder",
                        Size = 0,
                        LastModified = dir.LastWriteTime,
                        Permission = GetUnixPermissionSafe(dir)
                    });
                }

                int remaining = pageSize - dirsToTake.Count;
                if (remaining > 0)
                {
                    var filesToTake = fileList.Take(remaining).ToList();
                    foreach (var file in filesToTake)
                    {
                        pagedItems.Add(new FileItem
                        {
                            Name = file.Name,
                            Type = "file",
                            Size = file.Length,
                            LastModified = file.LastWriteTime,
                            Permission = GetUnixPermissionSafe(file)
                        });
                    }
                }
            }
            else
            {
                int fileSkip = skip - dirList.Count;
                var filesToTake = fileList.Skip(fileSkip).Take(pageSize).ToList();
                foreach (var file in filesToTake)
                {
                    pagedItems.Add(new FileItem
                    {
                        Name = file.Name,
                        Type = "file",
                        Size = file.Length,
                        LastModified = file.LastWriteTime,
                        Permission = GetUnixPermissionSafe(file)
                    });
                }
            }

            return Ok(new ApiResponse<PagedFilesResult>
            {
                Code = 200,
                Message = "获取成功",
                Data = new PagedFilesResult
                {
                    Total = totalCount,
                    Items = pagedItems
                }
            });
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(403, new ApiResponse<object>
            {
                Code = 403,
                Message = "系统权限不足，无法访问该目录"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiResponse<object>
            {
                Code = 500,
                Message = $"读取目录失败: {ex.Message}"
            });
        }
    }
    
    // 修改文件权限
    [HttpPost("instance/{id}/chmod")]
    public IActionResult ChangeFileMode(uint id, [FromBody] ChmodRequest request)
    {
        if (!IConfigBase.UserList.HasResourcePermission(User?.FindFirst("UserId")?.Value ?? "", "server", (int)id))
            return NotFound(ApiResponseService.NotFound());
        
        if (PlatFormServices.GetOs() == "Windows")
        {
            return BadRequest(new ApiResponse<object> { Code = 400, Message = "当前操作系统不支持修改文件权限" });
        }

        var server = IConfigBase.ServerList.GetServer(id);
        if (server == null) return NotFound(new ApiResponse<object> { Code = 404, Message = "实例不存在" });

        var check = FileUtils.GetSafePath(server.Base, request.Path);
        if (!check.IsSafe) return BadRequest(new ApiResponse<object> { Code = 403, Message = check.Message });

        try
        {
            string fullPath = check.FullPath;
            
            if (!Directory.Exists(fullPath) && !System.IO.File.Exists(fullPath))
            {
                return NotFound(new ApiResponse<object> { Code = 404, Message = "文件或目录不存在" });
            }

            int modeInt = Convert.ToInt32(request.Mode, 8);
            UnixFileMode modeEnum = (UnixFileMode)modeInt;

            if (Directory.Exists(fullPath))
            {
                // 递归修改文件夹权限
                SetUnixFileModeRecursive(fullPath, modeEnum);
            }
            else
            {
                System.IO.File.SetUnixFileMode(fullPath, modeEnum);
            }

            return Ok(new ApiResponse<object> { Code = 200, Message = "权限修改成功" });
        }
        catch (FormatException)
        {
            return BadRequest(new ApiResponse<object> { Code = 400, Message = "权限格式错误" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiResponse<object> { Code = 500, Message = $"修改权限失败: {ex.Message}" });
        }
    }

    // 递归设置权限辅助方法
    private void SetUnixFileModeRecursive(string path, UnixFileMode mode)
    {
        // 设置当前目录权限
        System.IO.File.SetUnixFileMode(path, mode);

        // 遍历所有文件
        foreach (var file in Directory.GetFiles(path))
        {
            System.IO.File.SetUnixFileMode(file, mode);
        }

        // 递归遍历子目录
        foreach (var dir in Directory.GetDirectories(path))
        {
            SetUnixFileModeRecursive(dir, mode);
        }
    }

    // 重命名文件或文件夹
    [HttpPost("instance/{id}/rename")]
    public IActionResult RenameFile(uint id, [FromBody] RenameFileRequest request)
    {
        if (!IConfigBase.UserList.HasResourcePermission(User?.FindFirst("UserId")?.Value ?? "", "server", (int)id))
            return NotFound(ApiResponseService.NotFound());
        
        var server = IConfigBase.ServerList.GetServer(id);
        if (server == null) return NotFound(new ApiResponse<object> { Code = 404, Message = "实例不存在" });
        
        var checkSource = FileUtils.GetSafePath(server.Base, request.OldPath);
        if (!checkSource.IsSafe) return BadRequest(new ApiResponse<object> { Code = 403, Message = checkSource.Message });

        var checkDest = FileUtils.GetSafePath(server.Base, request.NewPath);
        if (!checkDest.IsSafe) return BadRequest(new ApiResponse<object> { Code = 403, Message = checkDest.Message });

        try
        {
            if (Directory.Exists(checkSource.FullPath))
            {
                Directory.Move(checkSource.FullPath, checkDest.FullPath);
            }
            else if (System.IO.File.Exists(checkSource.FullPath))
            {
                System.IO.File.Move(checkSource.FullPath, checkDest.FullPath);
            }
            else
            {
                return NotFound(new ApiResponse<object> { Code = 404, Message = "源文件或目录不存在" });
            }

            return Ok(new ApiResponse<object> { Code = 200, Message = "重命名成功" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiResponse<object> { Code = 500, Message = $"重命名失败: {ex.Message}" });
        }
    }

    // 删除文件或文件夹
    [HttpPost("instance/{id}/delete")]
    public IActionResult DeleteFiles(uint id, [FromBody] DeleteFileRequest request)
    {
        if (!IConfigBase.UserList.HasResourcePermission(User?.FindFirst("UserId")?.Value ?? "", "server", (int)id))
            return NotFound(ApiResponseService.NotFound());
        
        var server = IConfigBase.ServerList.GetServer(id);
        if (server == null) return NotFound(new ApiResponse<object> { Code = 404, Message = "实例不存在" });

        int successCount = 0;
        int failCount = 0;

        foreach (var relativePath in request.Paths)
        {
            var check = FileUtils.GetSafePath(server.Base, relativePath);
            if (!check.IsSafe) 
            {
                failCount++;
                continue; 
            }

            try
            {
                if (Directory.Exists(check.FullPath))
                {
                    Directory.Delete(check.FullPath, true);
                    successCount++;
                }
                else if (System.IO.File.Exists(check.FullPath))
                {
                    System.IO.File.Delete(check.FullPath);
                    successCount++;
                }
                else
                {
                    successCount++; 
                }
            }
            catch
            {
                failCount++;
            }
        }

        return Ok(new ApiResponse<object> 
        { 
            Code = 200, 
            Message = $"删除完成: 成功 {successCount} 个，失败 {failCount} 个" 
        });
    }
    
    [HttpPost("instance/{id}/upload")]
    public IActionResult SaveUploadedFile(uint id, [FromBody] SaveUploadRequest request)
    {
        if (!IConfigBase.UserList.HasResourcePermission(User?.FindFirst("UserId")?.Value ?? "", "server", (int)id))
            return NotFound(ApiResponseService.NotFound());
        
        var server = IConfigBase.ServerList.GetServer(id);
        if (server == null) return NotFound(new ApiResponse<object> { Code = 404, Message = "实例不存在" });

        string tempBasePath = Path.Combine(IConfigBase.GetAppDataPath(), "Temp", "Uploads");
        string tempFilePath = Path.Combine(tempBasePath, request.UploadId + ".tmp");

        if (!System.IO.File.Exists(tempFilePath))
        {
            return NotFound(new ApiResponse<object> { Code = 404, Message = "上传会话已过期或文件不存在" });
        }

        string relativePath = string.IsNullOrEmpty(request.CurrentPath) 
            ? request.FileName 
            : Path.Combine(request.CurrentPath, request.FileName);

        var check = FileUtils.GetSafePath(server.Base, relativePath);
        if (!check.IsSafe)
        {
            try { System.IO.File.Delete(tempFilePath); } catch { }
            return BadRequest(new ApiResponse<object> { Code = 403, Message = check.Message });
        }

        string targetPath = check.FullPath;

        try
        {
            string? targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }
            
            if (System.IO.File.Exists(targetPath))
            {
                System.IO.File.Delete(targetPath);
            }

            System.IO.File.Move(tempFilePath, targetPath,true);

            return Ok(new ApiResponse<object> 
            { 
                Code = 200, 
                Message = "文件保存成功" 
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiResponse<object> 
            { 
                Code = 500, 
                Message = $"文件移动失败: {ex.Message}" 
            });
        }
    }

    // 下载或在线预览文件
    [HttpGet("instance/{id}/download")]
    public IActionResult DownloadFile(uint id, [FromQuery] string path, [FromQuery] bool inline = false)
    {
        if (!IConfigBase.UserList.HasResourcePermission(User?.FindFirst("UserId")?.Value ?? "", "server", (int)id))
            return NotFound(ApiResponseService.NotFound());
        
        var server = IConfigBase.ServerList.GetServer(id);
        if (server == null) return NotFound("实例不存在");

        var check = FileUtils.GetSafePath(server.Base, path);
        if (!check.IsSafe) return BadRequest("非法路径");

        string targetPath = check.FullPath;
        if (!System.IO.File.Exists(targetPath)) return NotFound("文件不存在");

        if (Directory.Exists(targetPath)) return BadRequest("无法直接下载文件夹");

        if (inline)
        {
            if (!_contentTypeProvider.TryGetContentType(targetPath, out var contentType))
            {
                contentType = "application/octet-stream";
            }
            return PhysicalFile(
                targetPath,
                contentType,
                enableRangeProcessing: true
            );
        }

        return PhysicalFile(
            targetPath,
            "application/octet-stream",
            Path.GetFileName(targetPath),
            enableRangeProcessing: true
        );
    }

    // 获取图片缩略图
    [HttpGet("instance/{id}/thumbnail")]
    public async Task<IActionResult> GetFileThumbnail(uint id, [FromQuery] string path, [FromQuery] int size = 256)
    {
        if (!IConfigBase.UserList.HasResourcePermission(User?.FindFirst("UserId")?.Value ?? "", "server", (int)id))
            return NotFound(ApiResponseService.NotFound());

        var server = IConfigBase.ServerList.GetServer(id);
        if (server == null) return NotFound("实例不存在");

        if (string.IsNullOrEmpty(path)) return BadRequest("路径不能为空");

        var check = FileUtils.GetSafePath(server.Base, path);
        if (!check.IsSafe) return BadRequest("非法路径");

        string targetPath = check.FullPath;
        if (!System.IO.File.Exists(targetPath)) return NotFound("文件不存在");

        string ext = Path.GetExtension(targetPath).ToLowerInvariant();
        if (ext == ".svg")
        {
            Response.Headers.CacheControl = "public, max-age=604800";
            return PhysicalFile(targetPath, "image/svg+xml");
        }

        var supportedImageExts = new HashSet<string> { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".ico", ".tiff", ".tga" };
        if (!supportedImageExts.Contains(ext))
        {
            return BadRequest("不支持生成该格式的缩略图");
        }

        size = Math.Clamp(size, 32, 1024);

        try
        {
            var fileInfo = new FileInfo(targetPath);
            long ticks = fileInfo.LastWriteTimeUtc.Ticks;
            long fileLength = fileInfo.Length;

            // MD5 哈希  path + ticks + fileLength + size 作为缓存key
            string cacheKey;
            using (var md5 = MD5.Create())
            {
                byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes($"{path}_{ticks}_{fileLength}_{size}"));
                cacheKey = Convert.ToHexString(hashBytes).ToLowerInvariant();
            }

            string cacheDir = Path.Combine(IConfigBase.GetAppDataPath(), "Temp", "Thumbnails", id.ToString());
            if (!Directory.Exists(cacheDir))
            {
                Directory.CreateDirectory(cacheDir);
            }

            string cacheFile = Path.Combine(cacheDir, $"{cacheKey}.webp");
            if (System.IO.File.Exists(cacheFile))
            {
                Response.Headers.CacheControl = "public, max-age=604800, stale-while-revalidate=86400";
                return PhysicalFile(cacheFile, "image/webp");
            }

            // 生成缩略图
            using (var image = await SixLabors.ImageSharp.Image.LoadAsync(targetPath))
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new SixLabors.ImageSharp.Size(size, size),
                    Mode = ResizeMode.Crop
                }));

                // 保存 WebP
                await image.SaveAsWebpAsync(cacheFile);
            }

            Response.Headers.CacheControl = "public, max-age=604800, stale-while-revalidate=86400";
            return PhysicalFile(cacheFile, "image/webp");
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"缩略图生成失败: {ex.Message}");
        }
    }

    // 复制文件或文件夹
    [HttpPost("instance/{id}/copy")]
    public IActionResult CopyFiles(uint id, [FromBody] BatchOperationRequest request)
    {
        if (!IConfigBase.UserList.HasResourcePermission(User?.FindFirst("UserId")?.Value ?? "", "server", (int)id))
            return NotFound(ApiResponseService.NotFound());
        
        var server = IConfigBase.ServerList.GetServer(id);
        if (server == null) return NotFound(new ApiResponse<object> { Code = 404, Message = "实例不存在" });

        // 验证目标目录是否安全
        var checkTarget = FileUtils.GetSafePath(server.Base, request.TargetPath);
        if (!checkTarget.IsSafe) return BadRequest(new ApiResponse<object> { Code = 403, Message = "目标路径非法: " + checkTarget.Message });

        string targetBaseDir = checkTarget.FullPath;
        if (!Directory.Exists(targetBaseDir))
        {
            return NotFound(new ApiResponse<object> { Code = 404, Message = "目标目录不存在" });
        }

        int successCount = 0;
        int failCount = 0;
        List<string> errors = new List<string>();

        foreach (var sourceRelPath in request.SourcePaths)
        {
            // 验证源路径是否安全
            var checkSource = FileUtils.GetSafePath(server.Base, sourceRelPath);
            if (!checkSource.IsSafe)
            {
                failCount++;
                errors.Add($"路径非法: {sourceRelPath}");
                continue;
            }

            string sourcePath = checkSource.FullPath;
            
            // 计算最终的目标路径
            string fileName = Path.GetFileName(sourcePath);
            // 如果是根目录无法获取文件名，跳过
            if (string.IsNullOrEmpty(fileName)) 
            {
                failCount++;
                continue;
            }

            string destPath = Path.Combine(targetBaseDir, fileName);

            // 防止复制到自己内部
            if (destPath.StartsWith(sourcePath + Path.DirectorySeparatorChar))
            {
                failCount++;
                errors.Add($"无法将文件夹复制到其子目录: {fileName}");
                continue;
            }

            try
            {
                if (Directory.Exists(sourcePath))
                {
                    // 递归复制文件夹
                    CopyDirectory(sourcePath, destPath);
                    successCount++;
                }
                else if (System.IO.File.Exists(sourcePath))
                {
                    // 复制文件 (默认覆盖)
                    System.IO.File.Copy(sourcePath, destPath, true);
                    successCount++;
                }
                else
                {
                    failCount++;
                    errors.Add($"源文件不存在: {fileName}");
                }
            }
            catch (Exception ex)
            {
                failCount++;
                errors.Add($"{fileName}: {ex.Message}");
            }
        }

        return Ok(new ApiResponse<object>
        {
            Code = 200,
            Message = $"复制完成: 成功 {successCount} 个，失败 {failCount} 个",
            Data = errors.Count > 0 ? errors : null
        });
    }

    // 移动文件或文件夹
    [HttpPost("instance/{id}/move")]
    public IActionResult MoveFiles(uint id, [FromBody] BatchOperationRequest request)
    {
        if (!IConfigBase.UserList.HasResourcePermission(User?.FindFirst("UserId")?.Value ?? "", "server", (int)id))
            return NotFound(ApiResponseService.NotFound());
        
        var server = IConfigBase.ServerList.GetServer(id);
        if (server == null) return NotFound(new ApiResponse<object> { Code = 404, Message = "实例不存在" });

        var checkTarget = FileUtils.GetSafePath(server.Base, request.TargetPath);
        if (!checkTarget.IsSafe) return BadRequest(new ApiResponse<object> { Code = 403, Message = "目标路径非法: " + checkTarget.Message });

        string targetBaseDir = checkTarget.FullPath;
        if (!Directory.Exists(targetBaseDir))
        {
            return NotFound(new ApiResponse<object> { Code = 404, Message = "目标目录不存在" });
        }

        int successCount = 0;
        int failCount = 0;
        List<string> errors = new List<string>();

        foreach (var sourceRelPath in request.SourcePaths)
        {
            var checkSource = FileUtils.GetSafePath(server.Base, sourceRelPath);
            if (!checkSource.IsSafe)
            {
                failCount++;
                continue;
            }

            string sourcePath = checkSource.FullPath;
            string fileName = Path.GetFileName(sourcePath);
            if (string.IsNullOrEmpty(fileName)) continue;

            string destPath = Path.Combine(targetBaseDir, fileName);

            // 检查源和目标是否相同
            if (sourcePath == destPath)
            {
                failCount++;
                errors.Add("源路径与目标路径相同");
                continue;
            }

            try
            {
                if (Directory.Exists(sourcePath))
                {
                    // 移动文件夹
                    Directory.Move(sourcePath, destPath);
                    successCount++;
                }
                else if (System.IO.File.Exists(sourcePath))
                {
                    // 如果目标文件已存在，先删除
                    if (System.IO.File.Exists(destPath))
                    {
                        System.IO.File.Delete(destPath);
                    }
                    System.IO.File.Move(sourcePath, destPath);
                    successCount++;
                }
                else
                {
                    failCount++;
                }
            }
            catch (Exception ex)
            {
                failCount++;
                errors.Add($"{fileName}: {ex.Message}");
            }
        }

        return Ok(new ApiResponse<object>
        {
            Code = 200,
            Message = $"移动完成: 成功 {successCount} 个，失败 {failCount} 个",
            Data = errors.Count > 0 ? errors : null
        });
    }
    

    // 递归复制文件夹方法
    private void CopyDirectory(string sourceDir, string destinationDir)
    {
        var dir = new DirectoryInfo(sourceDir);
        if (!dir.Exists) throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");

        DirectoryInfo[] dirs = dir.GetDirectories();

        // 创建目标文件夹
        Directory.CreateDirectory(destinationDir);

        // 复制文件
        foreach (FileInfo file in dir.GetFiles())
        {
            string targetFilePath = Path.Combine(destinationDir, file.Name);
            file.CopyTo(targetFilePath, true);
        }

        // 递归复制子文件夹
        foreach (DirectoryInfo subDir in dirs)
        {
            string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
            CopyDirectory(subDir.FullName, newDestinationDir);
        }
    }

    private string GetUnixPermissionSafe(FileSystemInfo info)
    {
        if (PlatFormServices.GetOs() != "Windows")
        {
            try
            {
                return Convert.ToString((int)info.UnixFileMode, 8);
            }
            catch
            {
                return "Unknown";
            }
        }
        return "";
    }
}