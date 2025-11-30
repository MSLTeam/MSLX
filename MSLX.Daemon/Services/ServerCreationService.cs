using System.Formats.Tar;
using System.IO.Compression;
using Downloader;
using Microsoft.AspNetCore.SignalR;
using MSLX.Daemon.Hubs;
using MSLX.Daemon.Models;
using MSLX.Daemon.Models.Tasks;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.BackgroundTasks;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json.Linq;

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
                    // 统一错误处理，一行代码搞定
                    await UpdateStatusAsync(task.ServerId, $"创建失败: {ex.Message}", -1, isError: true, exception: ex);
                }
            }
        }

        /// <summary>
        /// 处理服务器创建的完整逻辑
        /// </summary>
        private async Task ProcessCreation(CreateServerTask task, CancellationToken stoppingToken)
        {
            string serverIdStr = task.ServerId;
            int serverId = int.Parse(task.ServerId);
            var request = task.Request;

            _logger.LogInformation("开始处理任务: ServerId {ServerId}", serverId);

            await UpdateStatusAsync(serverIdStr, "创建任务已开始，正在初始化...", 0);

            // 服务器信息
            McServerInfo.ServerInfo server = new McServerInfo.ServerInfo
            {
                ID = serverId,
                Name = request.name,
                Base = request.path ?? Path.Combine(ConfigServices.GetAppDataPath(), "DaemonData", "Servers", serverIdStr),
                Java = request.java ?? "java",
                Core = request.core,
                MinM = request.minM,
                MaxM = request.maxM,
                Args = request.args ?? ""
            };

            ConfigServices.ServerList.CreateServer(server);
            _logger.LogInformation("服务器 {ServerId} 配置已创建。", serverId);

            // 这里处理Java的在线下载
            if (!string.IsNullOrEmpty(request.java) && request.java.Contains("MSLX://Java/"))
            {
                await UpdateStatusAsync(serverIdStr, "正在处理Java环境···", 0);
                string javaVersion = request.java.Replace("MSLX://Java/", "");
                string javaBaseDir = Path.Combine(ConfigServices.GetAppDataPath(), "DaemonData", "Tools", "Java");
                string javaPath = Path.Combine(javaBaseDir, javaVersion, "bin", PlatFormServices.GetOs() == "Windows" ? "java.exe" : "java");

                if (File.Exists(javaPath))
                {
                    await UpdateStatusAsync(serverIdStr, $"Java {javaVersion} 已存在，无需下载！", 0);
                }
                else
                {
                    // 真得下载Java了
                    await UpdateStatusAsync(serverIdStr, $"准备开始下载Java {javaVersion}...", 0);
                    
                    var response = await MSLApi.GetAsync($"/download/jdk/{javaVersion}",
                        new Dictionary<string, string>
                        {
                            { "arch", PlatFormServices.GetOsArch().Replace("amd64","x64") },
                            { "os", PlatFormServices.GetOs().ToLower().Replace("os", "") }
                        });

                    if (response.IsSuccessStatusCode)
                    {
                        JObject downloadInfo = JObject.Parse(response.Content);
                        await UpdateStatusAsync(serverIdStr, $"成功获取到 Java {javaVersion} ({PlatFormServices.GetOsArch()} / {PlatFormServices.GetOs()}) 下载资源", 0);
                        
                        string fileName = PlatFormServices.GetOs() == "Windows" ? $"{javaVersion}.zip" : $"{javaVersion}.tar.gz";
                        string fullPath = Path.Combine(javaBaseDir, fileName);
                        Directory.CreateDirectory(javaBaseDir);

                        string? sha256 = downloadInfo["data"]?["sha256"]?.ToString();
                        string? downloadUrl = downloadInfo["data"]?["url"]?.ToString();

                        // 通用下载
                        bool downloadSuccess = await DownloadAndValidateAsync(
                            serverIdStr, 
                            downloadUrl, 
                            fullPath, 
                            $"Java {javaVersion}", 
                            sha256);

                        if (downloadSuccess)
                        {
                            // 下载校验成功，开始处理解压
                            try 
                            {
                                _logger.LogInformation("Java {javaVersion} 下载完成，开始智能解压...", javaVersion);
                                await UpdateStatusAsync(serverIdStr, $"正在配置 Java {javaVersion} 环境...", 99.99);
                                
                                await ExtractJavaSmartAsync(fullPath, javaVersion, javaBaseDir);
                                
                                await UpdateStatusAsync(serverIdStr, $"Java {javaVersion} 部署成功！", 99.9);
                            }
                            catch (Exception ex)
                            {
                                await UpdateStatusAsync(serverIdStr, $"Java 解压失败: {ex.Message}", -1, true, ex);
                            }
                        }
                        else
                        {
                            return; 
                        }
                    }
                    else
                    {
                        await UpdateStatusAsync(serverIdStr, $"下载 Java {javaVersion} 失败: {response.ResponseException}", -1, true);
                        return;
                    }
                }
            }

            // 检查下载
            await UpdateStatusAsync(serverIdStr, "服务器配置创建成功。正在检查核心文件...", null);

            if (!string.IsNullOrEmpty(request.coreUrl))
            {
                // 下载核心
                _logger.LogInformation("服务器 {ServerId} 需要下载核心: {Url}", serverId, request.coreUrl);
                await UpdateStatusAsync(serverIdStr, "开始下载核心文件...", 0);

                string fullPath = Path.Combine(server.Base, server.Core);
                Directory.CreateDirectory(server.Base);

                bool coreSuccess = await DownloadAndValidateAsync(
                    serverIdStr, 
                    request.coreUrl, 
                    fullPath, 
                    "服务端核心", 
                    request.coreSha256);

                if (coreSuccess)
                {
                    _logger.LogInformation("服务器 {ServerId} 核心下载完成。", serverId);
                    await UpdateStatusAsync(serverIdStr, "核心下载完成。服务器创建成功！", 100.0);
                }
                // 如果失败，通用方法里已经记录了 Log 和 Cache，这里无需额外操作
            }
            else
            {
                // 无需下载
                _logger.LogInformation("服务器 {ServerId} 无需下载，任务完成。", serverId);
                await UpdateStatusAsync(serverIdStr, "创建成功 (无需下载核心文件)。", 100);
            }
        }

        /// <summary>
        /// 智能解压 Java 逻辑
        /// </summary>
        private async Task ExtractJavaSmartAsync(string archivePath, string version, string baseDir)
        {
            string finalDestDir = Path.Combine(baseDir, version);
            string tempExtractPath = Path.Combine(Path.GetTempPath(), $"MSLX_Java_{Guid.NewGuid()}");

            try
            {
                Directory.CreateDirectory(tempExtractPath);

                // 解压
                if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    ZipFile.ExtractToDirectory(archivePath, tempExtractPath);
                }
                else if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
                {
                    using var fs = File.OpenRead(archivePath);
                    using var gzip = new GZipStream(fs, CompressionMode.Decompress);
                    TarFile.ExtractToDirectory(gzip, tempExtractPath, true);
                }

                // 寻找真正的java！
                string javaExec = PlatFormServices.GetOs() == "Windows" ? "java.exe" : "java";
                string? validJavaHome = Directory.GetFiles(tempExtractPath, javaExec, SearchOption.AllDirectories)
                    .Select(f => Directory.GetParent(Path.GetDirectoryName(f)!)?.FullName) // 找到 bin 的上一级
                    .FirstOrDefault();

                if (string.IsNullOrEmpty(validJavaHome))
                {
                    throw new DirectoryNotFoundException($"无法在压缩包中定位到包含 {javaExec} 的 bin 目录。");
                }

                // 移动有效内容到最终目录
                if (Directory.Exists(finalDestDir)) Directory.Delete(finalDestDir, true);
                Directory.CreateDirectory(finalDestDir);

                // 递归复制
                foreach (string dirPath in Directory.GetDirectories(validJavaHome, "*", SearchOption.AllDirectories))
                {
                    Directory.CreateDirectory(dirPath.Replace(validJavaHome, finalDestDir));
                }
                foreach (string newPath in Directory.GetFiles(validJavaHome, "*.*", SearchOption.AllDirectories))
                {
                    File.Copy(newPath, newPath.Replace(validJavaHome, finalDestDir), true);
                }

                // 针对 Linux/Mac 赋予可执行权限
                if (PlatFormServices.GetOs() != "Windows")
                {
                    try
                    {
                        string javaBinPath = Path.Combine(finalDestDir, "bin", "java");
                        if (File.Exists(javaBinPath))
                        {
                            System.Diagnostics.Process.Start("chmod", $"+x \"{javaBinPath}\"").WaitForExit();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("尝试设置 Java 执行权限失败: {Message}", ex.Message);
                    }
                }
            }
            finally
            {
                // 清理
                try { Directory.Delete(tempExtractPath, true); } catch { }
                try { File.Delete(archivePath); } catch { }
            }
        }

        /// <summary>
        /// 通用下载与校验
        /// </summary>
        private async Task<bool> DownloadAndValidateAsync(string serverId, string? url, string savePath, string itemName, string? sha256)
        {
            if (string.IsNullOrEmpty(url)) return false;

            var downloadOpt = new DownloadConfiguration()
            {
                ChunkCount = 8,
                ParallelDownload = true
            };

            var downloader = new DownloadService(downloadOpt);
            DateTime lastReportTime = DateTime.MinValue;
            const int throttleMilliseconds = 1000;
            
            // 使用 TaskCompletionSource 来桥接事件和异步流
            var tcs = new TaskCompletionSource<bool>();

            downloader.DownloadProgressChanged += async (s, e) =>
            {
                if (DateTime.UtcNow - lastReportTime > TimeSpan.FromMilliseconds(throttleMilliseconds))
                {
                    lastReportTime = DateTime.UtcNow;
                    double roundedProgress = Math.Round(e.ProgressPercentage, 2);
                    // 仅推送状态，不记录大量日志防止刷屏
                    await UpdateStatusAsync(serverId, $"下载 {itemName} 中... {roundedProgress}%", 
                        roundedProgress == 100 ? 99.9 : roundedProgress, logToConsole: false);
                }
            };

            downloader.DownloadFileCompleted += async (s, e) =>
            {
                if (e.Cancelled)
                {
                    await UpdateStatusAsync(serverId, $"{itemName} 下载被取消。", -1, isError: true);
                    tcs.TrySetResult(false);
                }
                else if (e.Error != null)
                {
                    await UpdateStatusAsync(serverId, $"{itemName} 下载失败: {e.Error.Message}", -1, isError: true, exception: e.Error);
                    tcs.TrySetResult(false);
                }
                else
                {
                    // 校验文件完整性
                    bool isHashMismatch = !string.IsNullOrEmpty(sha256)
                                          && !await FileUtils.ValidateFileSha256Async(savePath, sha256);

                    if (isHashMismatch)
                    {
                        await UpdateStatusAsync(serverId, $"{itemName} 下载失败: 校验文件完整性失败！", -1, isError: true);
                        try { File.Delete(savePath); } catch { }
                        tcs.TrySetResult(false);
                    }
                    else
                    {
                        // 成功，但不在这里发送最终100%，由调用者决定下一步(例如解压)
                        tcs.TrySetResult(true);
                    }
                }
            };

            try
            {
                await downloader.DownloadFileTaskAsync(url, savePath);
                return await tcs.Task;
            }
            catch (Exception ex)
            {
                await UpdateStatusAsync(serverId, $"{itemName} 下载启动失败: {ex.Message}", -1, isError: true, exception: ex);
                return false;
            }
        }

        /// <summary>
        /// 封装的统一状态更新方法：日志 + 缓存 + SignalR
        /// </summary>
        /// <param name="serverId">服务器ID</param>
        /// <param name="message">消息内容</param>
        /// <param name="progress">进度 (0-100, -1=错误)</param>
        /// <param name="isError">是否为错误状态</param>
        /// <param name="exception">可选的异常信息</param>
        /// <param name="logToConsole">是否输出到控制台日志(默认True)</param>
        private async Task UpdateStatusAsync(string serverId, string message, double? progress, bool isError = false, Exception? exception = null, bool logToConsole = true)
        {
            // 处理日志
            if (logToConsole)
            {
                if (isError || exception != null)
                {
                    _logger.LogError(exception, "[{ServerId}] Error: {Message}", serverId, message);
                }
                else
                {
                    _logger.LogInformation("[{ServerId}] Status: {Message} (Progress: {Progress})", serverId, message, progress);
                }
            }

            // 处理缓存 (错误状态和普通状态都缓存)
            var status = new CacheableStatus 
            { 
                Message = message, 
                Progress = progress ?? 0 
            };
            
            // 默认缓存10分钟
            _memoryCache.Set(serverId, status, TimeSpan.FromMinutes(10));

            // 发送 SignalR 通知
            await _hubContext.Clients.Group(serverId).SendAsync("StatusUpdate", serverId, message, progress);
        }
    }
}