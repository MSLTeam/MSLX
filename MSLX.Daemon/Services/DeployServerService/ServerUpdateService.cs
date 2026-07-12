using Downloader;
using Microsoft.AspNetCore.SignalR;
using MSLX.Daemon.Hubs;
using MSLX.Daemon.Services.DeployServerService;
using MSLX.Daemon.Utils.BackgroundTasks;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.Models.Tasks;

namespace MSLX.Daemon.Services.DeployServerService;

public class ServerUpdateService : BackgroundService
{
    private readonly ILogger<ServerUpdateService> _logger;
    private readonly IBackgroundTaskQueue<UpdateServerTask> _taskQueue;
    private readonly IHubContext<UpdateProgressHub> _hubContext;
    private readonly ServerDeploymentService _deployer; 

    public ServerUpdateService(
        ILogger<ServerUpdateService> logger,
        IBackgroundTaskQueue<UpdateServerTask> taskQueue,
        IHubContext<UpdateProgressHub> hubContext,
        ServerDeploymentService deployer)
    {
        _logger = logger;
        _taskQueue = taskQueue;
        _hubContext = hubContext;
        _deployer = deployer;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[MSLX-Service] 服务器更新后台服务已启动。");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // 获取任务
                var task = await _taskQueue.DequeueTaskAsync(stoppingToken);

                // 执行业务逻辑
                try
                {
                    await ProcessUpdate(task);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "更新任务异常: {ServerId}", task?.ServerId);

                    if (task != null)
                    {
                        await _hubContext.Clients.Group(task.ServerId)
                            .SendAsync("UpdateStatus", "系统内部错误", -1, true);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[MSLX-Service] 服务器更新服务已停止（正常退出）。");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "[MSLX-Service] 服务器更新服务发生致命错误。");
        }
    }

    private async Task ProcessUpdate(UpdateServerTask task)
    {
        var req = task.Request;
        string sid = task.ServerId;

        // 定义回调
        ServerDeploymentService.ReportProgress report = async (msg, progress, isErr, ex) =>
        {
             await _hubContext.Clients.Group(sid).SendAsync("UpdateStatus", msg, progress, isErr);
             if(isErr && ex != null) _logger.LogError(ex, msg);
        };

        await report("正在准备更新...", 0);

        try 
        {
            var server = IConfigBase.ServerList.GetServer((uint)req.ID);
            if (server == null) 
            {
                await report("找不到服务器配置", -1, true);
                return;
            }

            // 更新基础配置
            server.Name = req.Name;
            server.MinM = req.MinM ?? server.MinM;
            server.MaxM = req.MaxM ?? server.MaxM;
            server.Args = req.Args ?? "";
            // server.Java = req.Java;
            server.Core = req.Core;
            server.Base = req.Base;
            server.ForceExitDelay = req.ForceExitDelay;
            server.BackupDelay = req.BackupDelay;
            server.BackupMaxCount = req.BackupMaxCount;
            server.BackupPath = req.BackupPath;
            server.StopCommand = req.StopCommand;
            server.MonitorPlayers = req.MonitorPlayers;
            server.AllowOriginASCIIColors = req.AllowOriginASCIIColors;
            server.YggdrasilApiAddr = req.YggdrasilApiAddr;
            server.RunOnStartup = req.RunOnStartup;
            server.AutoRestart = req.AutoRestart;
            server.IgnoreEula = req.IgnoreEula;
            server.ForceJvmUTF8 = req.ForceJvmUTF8;
            server.InputEncoding = req.InputEncoding;
            server.OutputEncoding = req.OutputEncoding;
            server.FileEncoding = req.FileEncoding;
            // docker的一堆配置
            server.DockerImage = req.DockerImage;
            server.DockerWorkingDir = req.DockerWorkingDir;
            server.DockerVolumes = req.DockerVolumes;
            server.DockerEnvVars = req.DockerEnvVars;
            server.DockerNetworkMode = req.DockerNetworkMode;
            server.DockerNetworkAlias = req.DockerNetworkAlias;
            server.DockerPorts = req.DockerPorts;
            server.DockerCpuPercentage = req.DockerCpuPercentage;
            server.DockerCpuCores = req.DockerCpuCores;
            server.DockerMaxMemoryMb = req.DockerMaxMemoryMb;
            server.DockerMaxSwapMb = req.DockerMaxSwapMb;
            server.DockerMaxStorage = req.DockerMaxStorage;
            server.DockerUploadRate = req.DockerUploadRate;
            server.DockerDownloadRate = req.DockerDownloadRate;
            server.DockerExtraArgs = req.DockerExtraArgs;
            server.DockerExtraHosts = req.DockerExtraHosts;
            try
            {
                server.ServerPropertiesPath = ServerPropertiesPathUtils.NormalizeRelativePath(req.ServerPropertiesPath);
                server.PluginsPath = ServerPropertiesPathUtils.NormalizeRelativePath(req.PluginsPath, "plugins", "插件目录路径必须是实例目录内的相对路径");
                server.ModsPath = ServerPropertiesPathUtils.NormalizeRelativePath(req.ModsPath, "mods", "模组目录路径必须是实例目录内的相对路径");
                server.WorldPath = ServerPropertiesPathUtils.NormalizeRelativePath(req.WorldPath, "world", "地图目录路径必须是实例目录内的相对路径");
                server.RegionPath = ServerPropertiesPathUtils.NormalizeRelativePath(req.RegionPath, "region", "Region 目录路径必须是地图目录内的相对路径");
            }
            catch (ArgumentException ex)
            {
                await report($"路径配置无效: {ex.Message}", -1, true);
                return;
            }

            IConfigBase.ServerList.UpdateServer(server);

            // 检查 Java
            if (!string.IsNullOrEmpty(req.Java))
            {
                await _deployer.EnsureJavaAsync(sid, req.Java, report);
                server.Java = req.Java; // 下载成功后再保存路径
            }

            // 检查 Core
            if (!string.IsNullOrEmpty(req.CoreUrl) || !string.IsNullOrEmpty(req.CoreFileKey))
            {
                string coreName = server.Core;
                
                await _deployer.DeployCoreAsync(sid, server.Base, coreName, req.CoreFileKey, req.CoreUrl, req.CoreSha256, report);
            }

            // NeoForge 安装
            if ((server.Core.Contains("forge") || server.Core.Contains("neoforge")) && server.Core.Contains(".jar") && !server.Core.Contains("arclight"))
            {
               string? newArgs = await _deployer.InstallForgeIfNeededAsync(sid, server.Base, server.Core, server.Java, report, server.DockerImage);
               if(newArgs != null) server.Core = newArgs;
            }

            // 保存最终配置
            IConfigBase.ServerList.UpdateServer(server);
            
            await report("更新完成！", 100);
        }
        catch
        {
             // 不用处理啥
        }
    }
}