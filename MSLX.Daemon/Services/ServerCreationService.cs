using Downloader;
using Microsoft.AspNetCore.SignalR;
using MSLX.Daemon.Hubs;
using MSLX.Daemon.Models;
using MSLX.Daemon.Models.Tasks;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.BackgroundTasks;
using Microsoft.Extensions.Caching.Memory; 

namespace MSLX.Daemon.Services
{
    /// <summary>
    /// 介系常驻的后台服务 (HostedService)
    /// 不断地从 IBackgroundTaskQueue 中拉取任务并执行
    /// </summary>
    public class ServerCreationService : BackgroundService
    {
        private readonly ILogger<ServerCreationService> _logger;
        private readonly IBackgroundTaskQueue _taskQueue;
        private readonly IHubContext<CreationProgressHub> _hubContext;
        private readonly IMemoryCache _memoryCache; 

        public ServerCreationService(
            ILogger<ServerCreationService> logger,
            IBackgroundTaskQueue taskQueue,
            IHubContext<CreationProgressHub> hubContext,
            IMemoryCache memoryCache) 
        {
            _logger = logger;
            _taskQueue = taskQueue;
            _hubContext = hubContext;
            _memoryCache = memoryCache;
        }

        /// <summary>
        /// BackgroundService 的主执行方法
        /// </summary>
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("服务器创建后台服务已启动。");

            while (!stoppingToken.IsCancellationRequested)
            {
                var task = await _taskQueue.DequeueTaskAsync(stoppingToken);

                try
                {
                    await ProcessCreation(task, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "处理创建任务 {ServerId} 时发生严重错误。", task.ServerId);

                    // 错误丢进缓存里
                    var errorStatus = new CacheableStatus { Message = $"创建失败: {ex.Message}", Progress = -1 };
                    _memoryCache.Set(task.ServerId.ToString(), errorStatus, TimeSpan.FromMinutes(10)); // 缓存10分钟
                    
                    await SendStatusAsync(task.ServerId.ToString(), errorStatus.Message, errorStatus.Progress);
                }
            }
        }

        /// <summary>
        /// 处理服务器创建的完整逻辑
        /// </summary>
        private async Task ProcessCreation(CreateServerTask task, CancellationToken stoppingToken)
        {
            int serverId = int.Parse(task.ServerId); 
            var request = task.Request;

            _logger.LogInformation("开始处理任务: ServerId {ServerId}", serverId);
            
            await SendStatusAsync(serverId.ToString(), "创建任务已开始，正在初始化...", 0);

            // 服务器信息
            McServerInfo.ServerInfo server = new McServerInfo.ServerInfo
            {
                ID = serverId, 
                Name = request.name,
                Base = request.path ?? Path.Combine(ConfigServices.GetAppDataPath(), "DaemonData", "Servers", serverId.ToString()), // <-- 这里用 string
                Java = request.java ?? "java",
                Core = request.core,
                MinM = request.minM,
                MaxM = request.maxM,
                Args = request.args ?? ""
            };

            ConfigServices.ServerList.CreateServer(server);
            _logger.LogInformation("服务器 {ServerId} 配置已创建。", serverId);
            
            // 检查下载
            await SendStatusAsync(serverId.ToString(), "服务器配置创建成功。正在检查核心文件...", null); 

            if (!string.IsNullOrEmpty(request.coreUrl))
            {
                // 下载核心
                _logger.LogInformation("服务器 {ServerId} 需要下载核心: {Url}", serverId, request.coreUrl);
                await SendStatusAsync(serverId.ToString(), "开始下载核心文件...", 0); 

                string fullPath = Path.Combine(server.Base, server.Core);
                Directory.CreateDirectory(server.Base); 

                var downloadOpt = new DownloadConfiguration()
                {
                    ChunkCount = 8, 
                    ParallelDownload = true
                };

                var downloader = new DownloadService(downloadOpt);

                DateTime lastReportTime = DateTime.MinValue;
                const int throttleMilliseconds = 1000; // 下载进度的返回频率

                // 进度事件
                downloader.DownloadProgressChanged += async (s, e) =>
                {
                    if (DateTime.UtcNow - lastReportTime > TimeSpan.FromMilliseconds(throttleMilliseconds))
                    {
                        lastReportTime = DateTime.UtcNow; 
                        double roundedProgress = Math.Round(e.ProgressPercentage, 2);
                        await SendStatusAsync(serverId.ToString(), $"下载服务端中... {roundedProgress}%", roundedProgress);
                    }
                };

                // 完成事件
                downloader.DownloadFileCompleted += async (s, e) =>
                {
                    CacheableStatus finalStatus;
                    if (e.Cancelled)
                    {
                        _logger.LogWarning("服务器 {ServerId} 核心下载被取消。", serverId);
                        finalStatus = new CacheableStatus { Message = "核心下载被取消。", Progress = -1 };
                    }
                    else if (e.Error != null)
                    {
                        _logger.LogError(e.Error, "服务器 {ServerId} 核心下载失败。", serverId);
                        finalStatus = new CacheableStatus { Message = $"核心下载失败: {e.Error.Message}", Progress = -1 };
                    }
                    else
                    {
                        // 在这里校验文件完整性
                        bool isHashMismatch = !string.IsNullOrEmpty(request.coreSha256) 
                                              && !await FileUtils.ValidateFileSha256Async(fullPath, request.coreSha256);

                        if (isHashMismatch)
                        {
                            // 失败
                            _logger.LogError("服务器 {ServerId} 核心下载校验失败。", serverId);
                            finalStatus = new CacheableStatus { Message = "核心下载失败: 校验文件完整性失败！", Progress = -1 };
                            try { File.Delete(fullPath); } catch { } 
                        }
                        else
                        {
                            // 成功 / 没传入校验值
                            _logger.LogInformation("服务器 {ServerId} 核心下载完成。", serverId);
                            finalStatus = new CacheableStatus { Message = "核心下载完成。服务器创建成功！", Progress = 100.0 };
                        }
                        
                    }
                    
                    // 无论下载成功或失败，都写入缓存
                    _memoryCache.Set(serverId.ToString(), finalStatus, TimeSpan.FromMinutes(10));
                    await SendStatusAsync(serverId.ToString(), finalStatus.Message, finalStatus.Progress);
                };

                // 开始异步下载
                try
                {
                    await downloader.DownloadFileTaskAsync(request.coreUrl, fullPath);
                }
                catch (Exception ex)
                {
                     _logger.LogError(ex, "服务器 {ServerId} 下载启动失败。", serverId);
                     var errorStatus = new CacheableStatus { Message = $"下载启动失败: {ex.Message}", Progress = -1 };
                     // 下载失败也写入缓存
                     _memoryCache.Set(serverId.ToString(), errorStatus, TimeSpan.FromMinutes(10));
                     await SendStatusAsync(serverId.ToString(), errorStatus.Message, errorStatus.Progress);
                }
            }
            else
            {
                //  无需下载
                _logger.LogInformation("服务器 {ServerId} 无需下载，任务完成。", serverId);

                // 8用下载 直接把状态丢进缓存
                var finalStatus = new CacheableStatus { Message = "创建成功 (无需下载核心文件)。", Progress = 100 };
                _memoryCache.Set(serverId.ToString(), finalStatus, TimeSpan.FromMinutes(10));
                
                await SendStatusAsync(serverId.ToString(), finalStatus.Message, finalStatus.Progress);
            }
        }

        /// <summary>
        /// 封装一个向 SignalR 组发送消息的辅助方法
        /// </summary>
        /// <param name="serverId">服务器ID (即 SignalR 组名)</param>
        /// <param name="message">要显示的消息</param>
        /// <param name="progress">进度 (0-100)，null=不确定, -1=错误, 100=完成</param>
        private async Task SendStatusAsync(string serverId, string message, double? progress)
        {
            // 同时记录到日志
            _logger.LogInformation("[{ServerId}] Status: {message} (Progress: {progress})", serverId, message, progress);

            // 向该 serverId 组内的所有客户端发送 "StatusUpdate" 事件
            await _hubContext.Clients.Group(serverId).SendAsync("StatusUpdate", serverId, message, progress);
        }
    }
}