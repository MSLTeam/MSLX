using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using MSLX.Daemon.Hubs;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.Daemon.Utils.BackgroundTasks;
using MSLX.SDK.Models;
using MSLX.SDK.Models.Tasks;

namespace MSLX.Daemon.Services;

public class ServerCreationService : BackgroundService
{
    private readonly ILogger<ServerCreationService> _logger;
    private readonly IBackgroundTaskQueue<CreateServerTask> _taskQueue;
    private readonly IHubContext<CreationProgressHub> _hubContext;
    private readonly IMemoryCache _memoryCache;
    private readonly ServerDeploymentService _deploymentService; // 注入服务端部署服务

    public ServerCreationService(
        ILogger<ServerCreationService> logger,
        IBackgroundTaskQueue<CreateServerTask> taskQueue,
        IHubContext<CreationProgressHub> hubContext,
        IMemoryCache memoryCache,
        ServerDeploymentService deploymentService) 
    {
        _logger = logger;
        _taskQueue = taskQueue;
        _hubContext = hubContext;
        _memoryCache = memoryCache;
        _deploymentService = deploymentService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[MSLX-Service] 服务器创建后台服务已启动。");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // 获取任务
                var task = await _taskQueue.DequeueTaskAsync(stoppingToken);

                // 处理业务逻辑
                try
                {
                    await ProcessCreation(task, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "处理任务失败: {ServerId}", task?.ServerId);

                    await UpdateStatusAsync(task.ServerId, $"创建流程异常中断: {ex.Message}", -1, true, ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[MSLX-Service] 收到停止信号，后台服务正在停止...");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "[MSLX-Service] 后台服务发生致命错误。");
        }
    }

    private async Task ProcessCreation(CreateServerTask task, CancellationToken stoppingToken)
    {
        string serverIdStr = task.ServerId;
        int serverId = int.Parse(task.ServerId);
        var request = task.Request;

        _logger.LogInformation("开始处理任务: ServerId {ServerId}", serverId);

        // 汇报进度适配器
        ServerDeploymentService.ReportProgress progressReporter = async (msg, prog, isErr, ex) =>
        {
            await UpdateStatusAsync(serverIdStr, msg, prog, isErr, ex);
        };

        await progressReporter("创建任务已开始，正在初始化...", 0);

        // 初始化基本信息
        var server = new McServerInfo.ServerInfo
        {
            ID = serverId,
            Name = request.name,
            Base = request.path ?? Path.Combine(IConfigBase.GetAppDataPath(), "Servers", serverIdStr),
            Java = request.java ?? "java",
            Core = request.core,
            MinM = request.minM,
            MaxM = request.maxM,
            Args = request.args ?? "",
            IgnoreEula = request.ignoreEula,
            InputEncoding = "utf-8",
            StopCommand = request.java == "none" ? ((request.args ?? "").Contains("bedrock_server") ? "stop" : "^c") : "stop",
            MonitorPlayers = request.java != "none", // 自定义模式下默认不开启玩家监控
            OutputEncoding = PlatFormServices.GetOs() == "Windows"? "gbk" : "utf-8",
            FileEncoding = PlatFormServices.GetOs() == "Windows"? "gbk" : "utf-8",
        };

        IConfigBase.ServerList.CreateServer(server);
        Directory.CreateDirectory(server.Base); 
        _logger.LogInformation("服务器 {ServerId} 基础目录已配置。", serverId);

        try
        {
            // 远程下载整合包
            if (!string.IsNullOrEmpty(request.packageUrl))
            {
                string tempPackageFileKey = await _deploymentService.DownloadPackageAsync(serverIdStr, request.packageUrl, request.packageSha256, progressReporter);
                await _deploymentService.DeployPackageAsync(serverIdStr, tempPackageFileKey, server.Base, progressReporter);
                await _deploymentService.ChmodBedrockServerAsync(serverIdStr,server.Base, progressReporter);
            }
            
            // 部署整合包（由于参数拦截 远程下载的话不可能进入这一步 无需额外处理了）
            if (!string.IsNullOrEmpty(request.packageFileKey))
            {
                await _deploymentService.DeployPackageAsync(serverIdStr, request.packageFileKey, server.Base, progressReporter);
            }

            // 部署 Java
            await _deploymentService.EnsureJavaAsync(serverIdStr, request.java, progressReporter);

            // 部署核心 用户上传/远程下载
            await _deploymentService.DeployCoreAsync(
                serverIdStr, 
                server.Base, 
                server.Core, 
                request.coreFileKey, 
                request.coreUrl, 
                request.coreSha256, 
                progressReporter
            );

            // 安装 NeoForge 
            string? newLaunchArgs = await _deploymentService.InstallForgeIfNeededAsync(
                serverIdStr, 
                server.Base, 
                server.Core, 
                server.Java, 
                progressReporter
            );

            // 更新启动参数
            if (!string.IsNullOrEmpty(newLaunchArgs))
            {
                server.Core = newLaunchArgs;
                IConfigBase.ServerList.UpdateServer(server);
                _logger.LogInformation("已更新 Forge 启动参数: {Args}", newLaunchArgs);
            }

            await progressReporter("服务器创建成功！", 100);
        }
        catch (Exception)
        {
            return; 
        }
    }

    /// <summary>
    /// 依然保留这个私有方法，用于处理SignalR和缓存
    /// </summary>
    private async Task UpdateStatusAsync(string serverId, string message, double? progress, bool isError = false, Exception? exception = null)
    {
        if (isError || exception != null)
            _logger.LogError(exception, "[{ServerId}] Error: {Message}", serverId, message);
        else
            _logger.LogInformation("[{ServerId}] Status: {Message} ({Progress}%)", serverId, message, progress);

        var status = new CacheableStatus { Message = message, Progress = progress ?? 0 };
        _memoryCache.Set(serverId, status, TimeSpan.FromMinutes(10));
        await _hubContext.Clients.Group(serverId).SendAsync("StatusUpdate", serverId, message, progress);
    }
}