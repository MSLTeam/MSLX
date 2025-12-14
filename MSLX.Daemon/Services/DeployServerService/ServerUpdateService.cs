using Microsoft.AspNetCore.SignalR;
using MSLX.Daemon.Hubs;
using MSLX.Daemon.Models.Tasks;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.BackgroundTasks;

namespace MSLX.Daemon.Services;

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
        _logger.LogInformation("服务器更新后台服务已启动。");

        while (!stoppingToken.IsCancellationRequested)
        {
            // 从队列拿任务
            var task = await _taskQueue.DequeueTaskAsync(stoppingToken);

            try
            {
                await ProcessUpdate(task);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新任务异常");
                // 发送错误通知给前端
                await _hubContext.Clients.Group(task.ServerId).SendAsync("UpdateStatus", "系统内部错误", -1, true);
            }
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
            var server = ConfigServices.ServerList.GetServer((uint)req.ID);
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
            server.BackupDelay = req.BackupDelay;
            server.BackupMaxCount = req.BackupMaxCount;
            server.BackupPath = req.BackupPath;
            server.YggdrasilApiAddr = req.YggdrasilApiAddr;
            server.RunOnStartup = req.RunOnStartup;
            server.AutoRestart = req.AutoRestart;
            server.InputEncoding = req.InputEncoding;
            server.OutputEncoding = req.OutputEncoding;
            server.FileEncoding = req.FileEncoding;
            ConfigServices.ServerList.UpdateServer(server);

            // 检查 Java
            if (!string.IsNullOrEmpty(req.Java))
            {
                await _deployer.EnsureJavaAsync(sid, req.Java, report);
                server.Java = req.Java; // 下载成功后再保存路径
            }

            // 检查 Core
            if (!string.IsNullOrEmpty(req.CoreUrl) || !string.IsNullOrEmpty(req.CoreFileKey))
            {
                string coreName = server.Core; // 简单的文件名处理逻辑，此处略，按需补充
                
                await _deployer.DeployCoreAsync(sid, server.Base, coreName, req.CoreFileKey, req.CoreUrl, req.CoreSha256, report);
            }

            // NeoForge 安装
            if ((server.Core.Contains("forge") || server.Core.Contains("neoforge")) && server.Core.Contains(".jar") && !server.Core.Contains("arclight"))
            {
               string? newArgs = await _deployer.InstallForgeIfNeededAsync(sid, server.Base, server.Core, server.Java, report);
               if(newArgs != null) server.Core = newArgs;
            }

            // 保存最终配置
            ConfigServices.ServerList.UpdateServer(server);
            
            await report("更新完成！", 100);
        }
        catch(Exception ex)
        {
             // 不用处理啥
        }
    }
}