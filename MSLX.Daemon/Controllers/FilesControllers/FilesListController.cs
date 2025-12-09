using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models;
using MSLX.Daemon.Models.Files;
using MSLX.Daemon.Utils;

namespace MSLX.Daemon.Controllers.FilesControllers;



[ApiController]
[Route("api/files")]
public class FilesListController : ControllerBase
{
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
            string rootPath = Path.GetFullPath(server.Base);
            string requestPath = path ?? "";
            string targetPath = Path.GetFullPath(Path.Combine(rootPath, requestPath));
            
            // 确保目标路径必须以根路径开头
            if (!targetPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new ApiResponse<object>
                {
                    Code = 403,
                    Message = "禁止访问非实例资源目录"
                });
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
}