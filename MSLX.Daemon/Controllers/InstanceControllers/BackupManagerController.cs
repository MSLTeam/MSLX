using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models;
using MSLX.Daemon.Models.Instance;
using MSLX.Daemon.Utils.ConfigUtils;

namespace MSLX.Daemon.Controllers.InstanceControllers;

[Route("api/instance/backups")]
[ApiController]
public class BackupManagerController : ControllerBase
{
    #region 辅助方法封装
    private string GetBackupDirectory(McServerInfo.ServerInfo server)
    {
        // 默认路径：实例根目录/mslx-backups
        string backupDir = Path.Combine(server.Base, "mslx-backups");

        if (server.BackupPath == "MSLX://Backup/Data")
        {
            backupDir = Path.Combine(IConfigBase.GetAppDataPath(), "Backups", $"Backups_{server.Name}_{server.ID}");
        }
        else if (server.BackupPath != "MSLX://Backup/Instance" && !string.IsNullOrEmpty(server.BackupPath))
        {
            backupDir = server.BackupPath;
        }
        return backupDir;
    }

    private List<object> GetBackupListFromPath(string backupDir)
    {
        if (!Directory.Exists(backupDir)) return new List<object>();

        var dirInfo = new DirectoryInfo(backupDir);
        var files = dirInfo.GetFiles("mslx-backup_*.zip");

        return files.Select(file =>
        {
            long len = file.Length;
            string formattedSize = len switch
            {
                >= 1073741824 => $"{len / 1024.0 / 1024.0 / 1024.0:F2} GB",
                >= 1048576 => $"{len / 1024.0 / 1024.0:F2} MB",
                >= 1024 => $"{len / 1024.0:F2} KB",
                _ => $"{len} Bytes"
            };

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
        .Cast<object>()
        .ToList();
    }

    private string GetCoreName(string core)
    {
        core = core?.ToLower() ?? "";
        if (core.Contains("neoforge")) return "neoforge";
        if (core.Contains("forge")) return "forge";
        if (core == "none") return "custom";
        return core;
    }
    #endregion

    // 特定实例的备份列表
    [HttpGet("{id}")]
    public IActionResult GetInstanceBackups(uint id)
    {
        try
        {
            var server = IConfigBase.ServerList.GetServer(id) ?? throw new Exception("找不到指定的服务器实例");
            string backupDir = GetBackupDirectory(server);
            var backups = GetBackupListFromPath(backupDir);

            return Ok(new ApiResponse<object> { Code = 200, Message = "获取成功", Data = backups });
        }
        catch (Exception ex)
        {
            return BadRequest(new ApiResponse<object> { Code = 400, Message = ex.Message });
        }
    }

    // 所有实例的备份列表
    [HttpGet("all")]
    public IActionResult GetAllBackups()
    {
        try
        {
            var servers = IConfigBase.ServerList.GetServerList();

            var result = servers.Select(server => new
            {
                Id = server.ID,
                Name = server.Name,
                Core = GetCoreName(server.Core), 
                BackupPath = GetBackupDirectory(server),
                Backups = GetBackupListFromPath(GetBackupDirectory(server))
            }).ToList();

            return Ok(new ApiResponse<object> { Code = 200, Message = "获取成功", Data = result });
        }
        catch (Exception ex)
        {
            return BadRequest(new ApiResponse<object> { Code = 400, Message = $"获取列表失败: {ex.Message}" });
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




