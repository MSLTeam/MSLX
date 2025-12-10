using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models;
using MSLX.Daemon.Models.Files;
using MSLX.Daemon.Utils;

namespace MSLX.Daemon.Controllers.FilesControllers;



[ApiController]
[Route("api/files")]
public class FilesListController : ControllerBase
{
    // 获取文件列表
    [HttpGet("instance/{id}/lists")]
    public IActionResult GetFilesList(uint id, [FromQuery] string? path = "")
    {
        // 获取服务器配置
        var server = ConfigServices.ServerList.GetServer(id);
        if (server == null)
        {
            return NotFound(new ApiResponse<object>
            {
                Code = 404,
                Message = "找不到指定的服务端实例"
            });
        }

        // 检查目录是否存在
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
                // 返回具体的错误信息
                return BadRequest(new ApiResponse<object> { Code = 403, Message = check.Message });
            }
        
            string targetPath = check.FullPath;

            if (!Directory.Exists(targetPath))
            {
                return NotFound(new ApiResponse<object> { Code = 404, Message = "目录不存在" });
            }

            // 检查目标目录是否存在
            if (!Directory.Exists(targetPath))
            {
                return NotFound(new ApiResponse<object>
                {
                    Code = 404,
                    Message = "请求的目录不存在"
                });
            }

            // 读取文件和目录信息
            var directoryInfo = new DirectoryInfo(targetPath);
            var resultList = new List<FileItem>();

            // 获取所有文件夹
            var dirs = directoryInfo.GetDirectories();
            foreach (var dir in dirs)
            {
                resultList.Add(new FileItem
                {
                    Name = dir.Name,
                    Type = "folder",
                    Size = 0, // 不算
                    LastModified = dir.LastWriteTime
                });
            }

            // 获取所有文件
            var files = directoryInfo.GetFiles();
            foreach (var file in files)
            {
                resultList.Add(new FileItem
                {
                    Name = file.Name,
                    Type = "file",
                    Size = file.Length,
                    LastModified = file.LastWriteTime
                });
            }

            // 排序：文件夹在上方，文件在下方，按名称排序
            var sortedList = resultList
                .OrderByDescending(x => x.Type == "folder") // 文件夹排前面
                .ThenBy(x => x.Name) // 然后按名字排
                .ToList();

            return Ok(new ApiResponse<List<FileItem>>
            {
                Code = 200,
                Message = "获取成功",
                Data = sortedList
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
    
    // 重命名文件或文件夹
    [HttpPost("instance/{id}/rename")]
    public IActionResult RenameFile(uint id, [FromBody] RenameFileRequest request)
    {
        var server = ConfigServices.ServerList.GetServer(id);
        if (server == null) return NotFound(new ApiResponse<object> { Code = 404, Message = "实例不存在" });
        
        var checkSource = FileUtils.GetSafePath(server.Base, request.OldPath);
        if (!checkSource.IsSafe) return BadRequest(new ApiResponse<object> { Code = 403, Message = checkSource.Message });

        // 完整相对路径
        var checkDest = FileUtils.GetSafePath(server.Base, request.NewPath);
        if (!checkDest.IsSafe) return BadRequest(new ApiResponse<object> { Code = 403, Message = checkDest.Message });

        try
        {
            if (Directory.Exists(checkSource.FullPath))
            {
                // 文件夹
                Directory.Move(checkSource.FullPath, checkDest.FullPath);
            }
            else if (System.IO.File.Exists(checkSource.FullPath))
            {
                // 文件
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
        var server = ConfigServices.ServerList.GetServer(id);
        if (server == null) return NotFound(new ApiResponse<object> { Code = 404, Message = "实例不存在" });

        int successCount = 0;
        int failCount = 0;

        // 支持批量删除
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
                    // 递归删除文件夹
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
                    // 文件本来就不存在，也算成功
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
        var server = ConfigServices.ServerList.GetServer(id);
        if (server == null) return NotFound(new ApiResponse<object> { Code = 404, Message = "实例不存在" });

        // 临时文件存在？
        string tempBasePath = Path.Combine(ConfigServices.GetAppDataPath(), "DaemonData", "Temp", "Uploads");
        string tempFilePath = Path.Combine(tempBasePath, request.UploadId + ".tmp");

        if (!System.IO.File.Exists(tempFilePath))
        {
            return NotFound(new ApiResponse<object> { Code = 404, Message = "上传会话已过期或文件不存在" });
        }

        // check
        string relativePath = string.IsNullOrEmpty(request.CurrentPath) 
            ? request.FileName 
            : Path.Combine(request.CurrentPath, request.FileName);

        var check = FileUtils.GetSafePath(server.Base, relativePath);
        if (!check.IsSafe)
        {
            // 安全拦截后 顺手清理临时文件
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
            
            // 移动文件
            if (System.IO.File.Exists(targetPath))
            {
                System.IO.File.Delete(targetPath);
            }

            // 移动
            System.IO.File.Move(tempFilePath, targetPath);

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
    
    // 下崽文件
    [HttpGet("instance/{id}/download")]
    public IActionResult DownloadFile(uint id, [FromQuery] string path)
    {
        var server = ConfigServices.ServerList.GetServer(id);
        if (server == null) return NotFound("实例不存在");
        
        var check = FileUtils.GetSafePath(server.Base, path);
        if (!check.IsSafe) return BadRequest("非法路径");

        string targetPath = check.FullPath;
        if (!System.IO.File.Exists(targetPath)) return NotFound("文件不存在");
        
        // 不支持下崽文件夹
        if (Directory.Exists(targetPath)) return BadRequest("无法直接下载文件夹");

        try
        {
            // 返回文件流
            var stream = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            
            // 获取文件名
            string fileName = Path.GetFileName(targetPath);

            // 返回 FileStreamResult
            return File(stream, "application/octet-stream", fileName);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"读取文件失败: {ex.Message}");
        }
    }
}