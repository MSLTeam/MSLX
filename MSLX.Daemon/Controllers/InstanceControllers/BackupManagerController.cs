using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models;
using MSLX.Daemon.Models.Instance;
using MSLX.Daemon.Utils.ConfigUtils;

namespace MSLX.Daemon.Controllers.InstanceControllers;

[Route("api/instance/backups")]
[ApiController]
public class BackupManagerController : ControllerBase
{
    [HttpGet("{id}")]
    public IActionResult GetInstanceBackups(uint id)
    {
        try
        {
            McServerInfo.ServerInfo server =
                IConfigBase.ServerList.GetServer(id) ?? throw new Exception("找不到指定的服务器实例");

            // 获取备份路径
            string backupDir = Path.Combine(server.Base, "mslx-backups");
            if (server.BackupPath != "MSLX://Backup/Instance")
            {
                if (server.BackupPath == "MSLX://Backup/Data")
                {
                    backupDir = Path.Combine(IConfigBase.GetAppDataPath(), "Backups",
                        $"Backups_{server.Name}_{id}");
                }
                else if (!string.IsNullOrEmpty(server.BackupPath))
                {
                    backupDir = Path.Combine(server.BackupPath);
                }
            }

            // 目录不存在
            if (!Directory.Exists(backupDir))
            {

                return Ok(new ApiResponse<List<object>>
                {
                    Code = 200,
                    Message = "获取成功",
                    Data = new List<object>()
                });
            }

            // 获取文件数据
            var dirInfo = new DirectoryInfo(backupDir);

            // mslx-backup_
            var files = dirInfo.GetFiles("mslx-backup_*.zip");

            var backupList = files.Select(file =>
            {
                // 文件大小
                string formattedSize;
                long len = file.Length;
                if (len > 1024 * 1024 * 1024) formattedSize = $"{len / (1024.0 * 1024.0 * 1024.0):F2} GB";
                else if (len > 1024 * 1024) formattedSize = $"{len / (1024.0 * 1024.0):F2} MB";
                else if (len > 1024) formattedSize = $"{len / 1024.0:F2} KB";
                else formattedSize = $"{len} Bytes";

                return new
                {
                    FileName = file.Name,
                    FileSize = file.Length,
                    FileSizeStr = formattedSize,
                    CreateTime = file.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
                    Timestamp = new DateTimeOffset(file.LastWriteTime).ToUnixTimeSeconds()
                };
            })
            .OrderByDescending(x => x.Timestamp)
            .ToList();

            return Ok(new ApiResponse<object>
            {
                Code = 200,
                Message = "获取成功",
                Data = backupList
            });

        }
        catch (Exception ex)
        {
            return BadRequest(new ApiResponse<object>
            {
                Code = 400,
                Message = $"{ex.Message}",
                Data = null
            });
        }
    }

    [HttpPost("delete")]
    public IActionResult DeleteBackup([FromBody] DeleteBackupRequest request)
    {

        try
        {
            McServerInfo.ServerInfo server =
                IConfigBase.ServerList.GetServer(request.Id) ?? throw new Exception("找不到指定的服务器实例");

            // 备份目录
            string backupDir = Path.Combine(server.Base, "mslx-backups");
            if (server.BackupPath != "MSLX://Backup/Instance")
            {
                if (server.BackupPath == "MSLX://Backup/Data")
                {
                    backupDir = Path.Combine(IConfigBase.GetAppDataPath(), "Backups",
                        $"Backups_{server.Name}_{request.Id}");
                }
                else if (!string.IsNullOrEmpty(server.BackupPath))
                {
                    backupDir = Path.Combine(server.BackupPath);
                }
            }

            string fullPath = Path.Combine(backupDir, request.FileName);

            if (!System.IO.File.Exists(fullPath))
            {
                return NotFound(new ApiResponse<object>
                {
                    Code = 404,
                    Message = "未找到指定的备份文件",
                    Data = null
                });
            }

            // 删除
            System.IO.File.Delete(fullPath);

            return Ok(new ApiResponse<object>
            {
                Code = 200,
                Message = "备份文件删除成功",
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new ApiResponse<object>
            {
                Code = 400,
                Message = $"删除失败: {ex.Message}",
                Data = null
            });
        }
    }

    [HttpGet("download")]
    public IActionResult DownloadBackup([FromQuery] uint id, [FromQuery] string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return BadRequest(new ApiResponse<object>
            {
                Code = 400,
                Message = "文件名不能为空",
                Data = null
            });
        }

        try
        {
            McServerInfo.ServerInfo server =
                IConfigBase.ServerList.GetServer(id) ?? throw new Exception("找不到指定的服务器实例");

            string backupDir = Path.Combine(server.Base, "mslx-backups");
            if (server.BackupPath != "MSLX://Backup/Instance")
            {
                if (server.BackupPath == "MSLX://Backup/Data")
                {
                    backupDir = Path.Combine(IConfigBase.GetAppDataPath(), "Backups",
                        $"Backups_{server.Name}_{id}");
                }
                else if (!string.IsNullOrEmpty(server.BackupPath))
                {
                    backupDir = Path.Combine(server.BackupPath);
                }
            }

            string safeFileName = Path.GetFileName(fileName);
            if (safeFileName != fileName)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Code = 400,
                    Message = "非法的文件名",
                    Data = null
                });
            }

            string fullPath = Path.Combine(backupDir, safeFileName);

            if (!System.IO.File.Exists(fullPath))
            {
                return NotFound(new ApiResponse<object>
                {
                    Code = 404,
                    Message = "文件不存在或已被删除",
                    Data = null
                });
            }

            // 返回文件流
            var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return File(fileStream, "application/octet-stream", safeFileName);
        }
        catch (Exception ex)
        {
            return BadRequest(new ApiResponse<object>
            {
                Code = 400,
                Message = $"下载失败: {ex.Message}",
                Data = null
            });
        }
    }
}




