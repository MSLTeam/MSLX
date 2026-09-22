using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.Events;
using MSLX.SDK.Interfaces;
using MSLX.SDK.IServices;
using MSLX.SDK.Models;
using System.IO.Compression;

namespace MSLX.Daemon.Services.InstanceServices;

/// <summary>
/// 实例备份服务：世界存档的保存指令编排、压缩打包、滚动清理。
/// 注册为 DI 单例。
/// </summary>
public class InstanceBackupService : IInstanceBackupService
{
    private readonly ILogger<InstanceBackupService> _logger;
    private readonly IMSLXEvents _events;
    private readonly InstanceStateStore _stateStore;
    private readonly InstanceConsoleService _console;
    private readonly IMCServerService _mcServer;

    public InstanceBackupService(
        ILogger<InstanceBackupService> logger,
        IMSLXEvents events,
        InstanceStateStore stateStore,
        InstanceConsoleService console,
        IMCServerService mcServer)
    {
        _logger = logger;
        _events = events;
        _stateStore = stateStore;
        _console = console;
        _mcServer = mcServer;
    }


    public bool StartBackupServer(uint instanceId)
    {
        if (_stateStore.Get(instanceId) is { } context)
        {
            _ = Task.Run(async () => await BackupServer(instanceId, context));
            return true;
        }

        return false;
    }

    public async Task BackupServer(uint instanceId, ServerContext context)
    {
        if (context.IsBackuping)
        {
            _logger.LogWarning($"[Backup] 忽略备份请求，实例 {instanceId} 正在备份中。");
            _console.RecordLog(instanceId, context, "[MSLX-Backup] 正在备份中，请勿重复操作。");
            return;
        }

        bool isBedrock = false;
        DateTime backupStartTime = DateTime.Now;
        McServerInfo.ServerInfo? server = null;

        var mc = _mcServer;
        try
        {
            context.IsBackuping = true;
            server = IConfigBase.ServerList.GetServer(instanceId);
            if (server == null) return;

            // 计算备份保存路径
            string backupDir = Path.Combine(server.Base, "mslx-backups"); // 默认是存档内
            if (server.BackupPath != "MSLX://Backup/Instance")
            {
                if (server.BackupPath == "MSLX://Backup/Data")
                {
                    backupDir = Path.Combine(IConfigBase.GetAppDataPath(), "Backups",
                        $"Backups_{server.Name}_{instanceId}");
                }
                else if (!string.IsNullOrEmpty(server.BackupPath))
                {
                    backupDir = Path.Combine(server.BackupPath);
                }
            }

            var backupStartingArgs = new BackupStartingEventArgs
            {
                InstanceId = instanceId,
                ServerInfo = server,
                BackupDirectory = backupDir,
                Timestamp = backupStartTime
            };
            _events.PublishBackupStarting(backupStartingArgs);
            if (backupStartingArgs.Cancel)
            {
                _console.RecordLog(instanceId, context, $"[MSLX-Backup] 备份已被插件取消: {backupStartingArgs.CancelReason ?? "无"}");
                _logger.LogInformation($"实例 [{instanceId}] 备份已被插件取消: {backupStartingArgs.CancelReason ?? "无"}");
                return;
            }

            // 拦截基岩版逻辑
            if (File.Exists(Path.Combine(server.Base, "bedrock_server")) ||
                File.Exists(Path.Combine(server.Base, "bedrock_server.exe")))
            {
                isBedrock = true;
            }

            if (mc.IsServerRunning(instanceId))
            {
                if (isBedrock)
                {
                    mc.SendCommand(instanceId, "save hold");
                    if (PlatFormServices.GetOs() == "Windows") // Windows下目前输入中文会乱码 暂时这么解决叭
                    {
                        mc.SendCommand(instanceId,
                            "tellraw @a {\"rawtext\":[{\"text\":\"[MSLX] Backup in progress ~\"}]}");
                    }
                    else
                    {
                        mc.SendCommand(instanceId,
                            "tellraw @a {\"rawtext\":[{\"text\":\"§e[§aMSLX§e] §b正在进行服务器存档备份，请勿关闭服务器哦，否则可能造成回档！备份期间不会影响正常游戏~\"}]}");
                    }

                    _console.RecordLog(instanceId, context, "[MSLX-Backup] 正在备份基岩版服务器存档...");
                    await Task.Delay(server.BackupDelay * 1000);
                }
                else
                {
                    mc.SendCommand(instanceId, "save-off");
                    await Task.Delay(1000);
                    mc.SendCommand(instanceId, "save-all");
                    mc.SendCommand(instanceId,
                        "tellraw @a [{\"text\":\"[\",\"color\":\"yellow\"},{\"text\":\"MSLX\",\"color\":\"green\"},{\"text\":\"]\",\"color\":\"yellow\"},{\"text\":\"正在进行服务器存档备份，请勿关闭服务器哦，否则可能造成回档！备份期间不会影响正常游戏~\",\"color\":\"aqua\"}]");
                    _console.RecordLog(instanceId, context, "[MSLX-Backup] 正在备份服务器存档...");

                    await Task.Delay(server.BackupDelay * 1000); // 等待延迟时间进行保存
                }
            }

            // 获取需要备份的内容
            string worldPath = isBedrock ? "worlds" : "world";
            if (!isBedrock)
            {
                var serverPropertiesPath = ServerPropertiesPathUtils.ResolveFullPath(server);
                if (File.Exists(serverPropertiesPath))
                {
                    try
                    {
                        dynamic config = ServerPropertiesLoader.Load(serverPropertiesPath,
                            FileUtils.GetFileEncodingByString(server.FileEncoding));
                        worldPath = config.level_name == "未知" ? "world" : config.level_name;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "读取 server.properties 的 level-name 失败，备份将使用默认 world 路径: {Path}", serverPropertiesPath);
                    }
                }
            }

            // 兼容插件端的文件夹分离模式
            string fullWorldPath = Path.Combine(server.Base, worldPath);
            string fullNetherPath = Path.Combine(server.Base, worldPath + "_nether");
            string fullEndPath = Path.Combine(server.Base, worldPath + "_the_end");

            // 备份列表
            var foldersToCompress = new List<string>();

            if (Directory.Exists(fullWorldPath)) foldersToCompress.Add(fullWorldPath);
            if (Directory.Exists(fullNetherPath)) foldersToCompress.Add(fullNetherPath);
            if (Directory.Exists(fullEndPath)) foldersToCompress.Add(fullEndPath);

            // 确保有文件夹需要备份
            if (foldersToCompress.Count == 0)
            {
                _logger.LogWarning("未找到任何世界存档文件夹（包括主世界、下界、末地），备份失败！");
                _events.PublishBackupFailed(new BackupFailedEventArgs
                {
                    InstanceId = instanceId,
                    ServerInfo = server,
                    ErrorMessage = "未找到任何世界存档文件夹（包括主世界、下界、末地），备份失败！",
                    Timestamp = DateTime.Now
                });

                if (mc.IsServerRunning(instanceId))
                {
                    if (isBedrock)
                    {
                        mc.SendCommand(instanceId, "save resume");
                        if (PlatFormServices.GetOs() == "Windows")
                        {
                            mc.SendCommand(instanceId,
                                "tellraw @a {\"rawtext\":[{\"text\":\"[MSLX] Backup failed !\"}]}");
                        }
                        else
                        {
                            mc.SendCommand(instanceId,
                                "tellraw @a {\"rawtext\":[{\"text\":\"§e[§aMSLX§e] §c备份失败！未找到任何世界存档文件夹！\"}]}");
                        }
                    }
                    else
                    {
                        mc.SendCommand(instanceId, "save-on");
                        mc.SendCommand(instanceId,
                            "tellraw @a [{\"text\":\"[\",\"color\":\"yellow\"},{\"text\":\"MSLX\",\"color\":\"green\"},{\"text\":\"]\",\"color\":\"yellow\"},{\"text\":\"备份失败！未找到任何世界存档文件夹！\",\"color\":\"red\"}]");
                    }
                }

                return;
            }

            string backupPath = Path.Combine(backupDir, $"mslx-backup_{DateTime.Now.ToString("yyyyMMdd_HHmmss")}.zip");
            if (!Directory.Exists(backupDir)) Directory.CreateDirectory(backupDir);

            // 最大备份存档限制
            int maxBackups = 20;
            if (server.BackupMaxCount > 0) maxBackups = server.BackupMaxCount;

            // 删除多余的备份
            try
            {
                var backupFiles = Directory.GetFiles(backupDir, "mslx-backup_*.zip")
                    .Select(path => new FileInfo(path))
                    .OrderBy(fi => fi.Name) // 按文件名排序，文件名早的=时间旧的
                    .ToList();

                if (maxBackups >= 1 && backupFiles.Count >= maxBackups)
                {
                    int filesToDeleteCount = backupFiles.Count - maxBackups + 1;
                    var filesToDelete = backupFiles.Take(filesToDeleteCount).ToList();

                    // 遍历删除最旧的文件
                    foreach (var fileToDelete in filesToDelete)
                    {
                        try
                        {
                            fileToDelete.Delete();
                            _events.PublishBackupDeleted(new BackupDeletedEventArgs
                            {
                                InstanceId = instanceId,
                                BackupFilePath = fileToDelete.FullName,
                                BackupFileName = fileToDelete.Name,
                                IsAutoRoll = true,
                                Timestamp = DateTime.Now
                            });
                            _console.RecordLog(instanceId, context, $"[MSLX-Backup] 已删除旧备份：{fileToDelete.Name}");
                        }
                        catch (Exception ex)
                        {
                            // 如果删除失败，仅发出警告，不中断整个备份过程
                            _console.RecordLog(instanceId, context, $"[MSLX-Backup] 删除旧备份 {fileToDelete.Name} 失败：{ex.Message}");
                            _logger.LogWarning($"删除旧备份 {fileToDelete.Name} 失败：{ex.ToString()}");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError($"删除多余的备份失败 {instanceId}, {e.Message}");
                _console.RecordLog(instanceId, context, $"[MSLX-Backup] 删除多余的备份失败：{e.Message}");
            }

            // 开始压缩
            _console.RecordLog(instanceId, context, $"[MSLX-Backup] 正在压缩服务器存档...");
            await using (FileStream zipToOpen = new FileStream(backupPath, FileMode.Create))
            {
                using (ZipArchive archive = new ZipArchive(zipToOpen, ZipArchiveMode.Create))
                {
                    foreach (var folderPath in foldersToCompress)
                    {
                        // 开始递归压缩
                        await CompressFolder(server.Base, folderPath, archive);
                    }
                }
            }

            // 输出备份信息
            if (mc.IsServerRunning(instanceId))
            {
                try
                {
                    FileInfo backupFileInfo = new FileInfo(backupPath);
                    string fileName = backupFileInfo.Name;
                    long fileSizeInBytes = backupFileInfo.Length;
                    string formattedSize;
                    if (fileSizeInBytes > 1024 * 1024 * 1024)
                    {
                        formattedSize = $"{fileSizeInBytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
                    }
                    else if (fileSizeInBytes > 1024 * 1024)
                    {
                        formattedSize = $"{fileSizeInBytes / (1024.0 * 1024.0):F2} MB";
                    }
                    else if (fileSizeInBytes > 1024)
                    {
                        formattedSize = $"{fileSizeInBytes / 1024.0:F2} KB";
                    }
                    else
                    {
                        formattedSize = $"{fileSizeInBytes} Bytes";
                    }

                    string tellrawMessage;

                    if (isBedrock)
                    {
                        tellrawMessage =
                            $"tellraw @a {{\"rawtext\":[{{\"text\":\"§e[§aMSLX§e]§b 服务器存档备份完成！\\n§7文件名: §f{fileName}\\n§7大小: §f{formattedSize}\"}}]}}";

                        mc.SendCommand(instanceId, "save resume");
                    }
                    else
                    {
                        tellrawMessage = $"tellraw @a [";
                        tellrawMessage += "{\"text\":\"[\",\"color\":\"yellow\"},";
                        tellrawMessage += "{\"text\":\"MSLX\",\"color\":\"green\"},";
                        tellrawMessage += "{\"text\":\"]\",\"color\":\"yellow\"},";
                        tellrawMessage += "{\"text\":\" 服务器存档备份完成！\\n\",\"color\":\"aqua\"},";
                        tellrawMessage += $"{{\"text\":\"文件名: \",\"color\":\"gray\"}},";
                        tellrawMessage += $"{{\"text\":\"{fileName}\",\"color\":\"white\"}},";
                        tellrawMessage += $"{{\"text\":\"\\n大小: \",\"color\":\"gray\"}},";
                        tellrawMessage += $"{{\"text\":\"{formattedSize}\",\"color\":\"white\"}}";
                        tellrawMessage += "]";

                        mc.SendCommand(instanceId, "save-on");
                    }

                    if (PlatFormServices.GetOs() == "Windows" && isBedrock)
                    {
                        mc.SendCommand(instanceId,
                            "tellraw @a {\"rawtext\":[{\"text\":\"[MSLX] Backup finished !\"}]}");
                    }
                    else
                    {
                        mc.SendCommand(instanceId, tellrawMessage);
                    }
                }
                catch (Exception ex)
                {
                    _console.RecordLog(instanceId, context, "[MSL备份] 无法获取备份文件信息：" + ex.Message);
                    _logger.LogWarning("无法获取备份文件信息：" + ex.ToString());

                    // 异常
                    if (isBedrock)
                    {
                        mc.SendCommand(instanceId, "save resume");
                        if (PlatFormServices.GetOs() == "Windows")
                        {
                            mc.SendCommand(instanceId,
                                "tellraw @a {\"rawtext\":[{\"text\":\"[MSLX] Backup finished !\"}]}");
                        }
                        else
                        {
                            mc.SendCommand(instanceId,
                                "tellraw @a {\"rawtext\":[{\"text\":\"§e[§aMSLX§e] §b服务器存档备份完成！\"}]}");
                        }
                    }
                    else
                    {
                        mc.SendCommand(instanceId, "save-on");
                        mc.SendCommand(instanceId,
                            "tellraw @a [{\"text\":\"[\",\"color\":\"yellow\"},{\"text\":\"MSLX\",\"color\":\"green\"},{\"text\":\"]\",\"color\":\"yellow\"},{\"text\":\"服务器存档备份完成！\",\"color\":\"aqua\"}]");
                    }
                }
            }

            _console.RecordLog(instanceId, context, $"[MSLX-Backup] 存档备份成功！已保存至：{backupPath}");
            _logger.LogInformation($"[MSLX-Backup] 存档备份成功！已保存至：{backupPath}");

            try
            {
                FileInfo backupFileInfo = new FileInfo(backupPath);
                long fileSize = backupFileInfo.Exists ? backupFileInfo.Length : 0;
                string fmtSize = fileSize switch
                {
                    >= 1073741824 => $"{fileSize / (1024.0 * 1024.0 * 1024.0):F2} GB",
                    >= 1048576 => $"{fileSize / (1024.0 * 1024.0):F2} MB",
                    >= 1024 => $"{fileSize / 1024.0:F2} KB",
                    _ => $"{fileSize} Bytes"
                };

                _events.PublishBackupCompleted(new BackupCompletedEventArgs
                {
                    InstanceId = instanceId,
                    ServerInfo = server,
                    BackupFilePath = backupPath,
                    BackupFileName = backupFileInfo.Name,
                    FileSizeBytes = fileSize,
                    FormattedSize = fmtSize,
                    Duration = DateTime.Now - backupStartTime,
                    Timestamp = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[MSLX-Backup] 发布 BackupCompleted 事件失败: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"备份服务器失败 {instanceId}, {ex.Message}");
            _events.PublishBackupFailed(new BackupFailedEventArgs
            {
                InstanceId = instanceId,
                ServerInfo = server,
                ErrorMessage = ex.Message,
                Exception = ex,
                Timestamp = DateTime.Now
            });
        }
        finally
        {
            context.IsBackuping = false;
            // 兜底：无论备份成功或失败，都确保服务端恢复自动保存（save-on / save resume）
            if (mc.IsServerRunning(instanceId))
            {
                if (isBedrock)
                {
                    mc.SendCommand(instanceId, "save resume");
                }
                else
                {
                    mc.SendCommand(instanceId, "save-on");
                }
            }
        }
    }

    // 递归压缩方法
    public static async Task CompressFolder(string rootPath, string currentPath, ZipArchive archive)
    {
        string[] files = Directory.GetFiles(currentPath);

        foreach (string file in files)
        {
            // 排除 session.lock
            if (Path.GetFileName(file).Equals("session.lock", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // 计算相对路径 (作为压缩包内的文件名)
            string entryName = Path.GetRelativePath(rootPath, file);

            try
            {
                // 共享只读打开
                await using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    // 在压缩包中创建条目
                    ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);

                    // 最后修改时间
                    entry.LastWriteTime = File.GetLastWriteTime(file);

                    // 文件流复制到压缩包条目流中
                    using (Stream entryStream = entry.Open())
                    {
                        await fs.CopyToAsync(entryStream);
                    }
                }
            }
            catch (IOException ex)
            {
                throw new IOException($"无法以共享只读模式打开文件 '{entryName}'。服务器施加了排他锁。错误: {ex.Message}", ex);
            }
        }

        // 递归处理子文件夹
        string[] folders = Directory.GetDirectories(currentPath);
        foreach (string folder in folders)
        {
            await CompressFolder(rootPath, folder, archive);
        }
    }


}
