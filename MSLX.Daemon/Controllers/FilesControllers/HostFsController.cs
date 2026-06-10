using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Utils;
using MSLX.SDK.Models;

namespace MSLX.Daemon.Controllers;

[ApiController]
[Route("api/files")]
[Authorize(Roles = "admin")] 
public class HostFsController : ControllerBase
{
    /// <summary>
    /// 获取宿主机本地文件列表
    /// </summary>
    /// <param name="path">绝对路径，留空则为程序运行目录</param>
    /// <param name="searchPattern">文件搜索规则，默认只找 zip</param>
    [HttpGet("root")]
    public IActionResult GetHostFilesList([FromQuery] string? path = "", [FromQuery] string searchPattern = "*.zip")
    {
        try
        {
            string targetPath = string.IsNullOrWhiteSpace(path) 
                ? Directory.GetCurrentDirectory() 
                : path;
            
            targetPath = Path.GetFullPath(targetPath);

            if (!Directory.Exists(targetPath))
            {
                return NotFound(new ApiResponse<object> 
                { 
                    Code = 404, 
                    Message = "指定的目录不存在" 
                });
            }

            var directoryInfo = new DirectoryInfo(targetPath);
            var resultList = new List<object>();

            // 遍历目录
            var dirs = directoryInfo.GetDirectories();
            foreach (var dir in dirs)
            {
                if ((dir.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden || 
                    (dir.Attributes & FileAttributes.System) == FileAttributes.System) 
                    continue;

                resultList.Add(new 
                {
                    Name = dir.Name,
                    Path = dir.FullName,
                    Type = "folder",
                    Size = 0,
                    LastModified = dir.LastWriteTime
                });
            }

            // 遍历文件
            var files = directoryInfo.GetFiles(searchPattern);
            foreach (var file in files)
            {
                if ((file.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden || 
                    (file.Attributes & FileAttributes.System) == FileAttributes.System) 
                    continue;

                resultList.Add(new 
                {
                    Name = file.Name,
                    Path = file.FullName,
                    Type = "file",
                    Size = file.Length,
                    LastModified = file.LastWriteTime
                });
            }

            // 排序
            var sortedList = resultList
                .OrderByDescending(x => (string)((dynamic)x).Type == "folder")
                .ThenBy(x => (string)((dynamic)x).Name)
                .ToList();

            // 计算父级目录
            string? parentPath = Directory.GetParent(targetPath)?.FullName;

            return Ok(new ApiResponse<object>
            {
                Code = 200,
                Message = "获取成功",
                Data = new 
                {
                    currentPath = targetPath,
                    parentPath = parentPath, // 当位于根目录时为 null
                    items = sortedList
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

    /// <summary>
    /// 获取当前系统的逻辑驱动器
    /// </summary>
    [HttpGet("drives")]
    public IActionResult GetDrives()
    {
        try
        {
            bool isWindows = PlatFormServices.GetOs() == "Windows";

            var drives = DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .Where(d => 
                {
                    // Windows 下全部显示没啥问题
                    if (isWindows) return true;

                    // 过滤掉底层系统挂载点
                    string path = d.RootDirectory.FullName;
                    return path == "/" || // 根目录
                           path.StartsWith("/Volumes/") || // macOS 外接U盘/硬盘/DMG镜像
                           path.StartsWith("/mnt/") || // Linux 挂载点
                           path.StartsWith("/media/"); // Linux 外接媒体
                })
                .Select(d => new 
                {
                    Name = d.Name,            
                    Path = d.RootDirectory.FullName,
                    Type = "drive",
                    VolumeLabel = string.IsNullOrWhiteSpace(d.VolumeLabel) ? d.Name : d.VolumeLabel,
                    TotalSize = d.TotalSize,
                    AvailableFreeSpace = d.AvailableFreeSpace
                })
                .ToList();

            return Ok(new ApiResponse<object>
            {
                Code = 200,
                Message = "获取成功",
                Data = drives
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiResponse<object>
            {
                Code = 500,
                Message = $"获取挂载点列表失败: {ex.Message}"
            });
        }
    }
}