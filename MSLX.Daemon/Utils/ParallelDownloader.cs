using Downloader;
using MSLX.Daemon.Utils.ConfigUtils;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;

namespace MSLX.Daemon.Utils
{
    public class ParallelDownloader
    {
        private readonly int _parallelCount;
        private readonly int _maxTryAgainOnFailure;
        private readonly bool _parallelDownload;
        private readonly TimeSpan? _inactivityTimeout;
        private readonly SemaphoreSlim _fileConcurrencySemaphore;
        private static readonly Lazy<HttpClient> SharedHttpClient = new(() =>
        {
            var handler = new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip,
                MaxConnectionsPerServer = 128,
                PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2)
            };
            var client = new HttpClient(handler, disposeHandler: true);
            client.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("MSLX", PlatFormServices.GetFormattedVersion()));
            client.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("Downloader", GetDownloaderVersion()));
            return client;
        });

        /// <summary>
        /// 创建并行下载器
        /// </summary>
        /// <param name="parallelCount">每个文件的分块并发数。0 表示从系统配置动态读取（默认）</param>
        /// <param name="maxSimultaneousFiles">最大同时下载文件数</param>
        public ParallelDownloader(int parallelCount = 0, int maxSimultaneousFiles = 3,
            int maxTryAgainOnFailure = 5, bool parallelDownload = true,
            int inactivityTimeoutSeconds = 0)
        {
            _fileConcurrencySemaphore = new SemaphoreSlim(maxSimultaneousFiles);
            _parallelCount = parallelCount;
            _maxTryAgainOnFailure = Math.Max(0, maxTryAgainOnFailure);
            _parallelDownload = parallelDownload;
            _inactivityTimeout = inactivityTimeoutSeconds > 0
                ? TimeSpan.FromSeconds(inactivityTimeoutSeconds)
                : null;
        }

        private DownloadConfiguration CreateConfig(int? parallelCountOverride, bool? parallelDownloadOverride)
        {
            int count = parallelCountOverride ??
                        (_parallelCount > 0 ? _parallelCount : GetConfiguredThreadCount());
            return new DownloadConfiguration
            {
                ChunkCount = count,
                ParallelDownload = parallelDownloadOverride ?? _parallelDownload,
                ParallelCount = count,
                MaxTryAgainOnFailure = _maxTryAgainOnFailure,
                EnableAutoResumeDownload = true,
                DownloadFileExtension = ".download",
                CustomHttpClientFactory = () => SharedHttpClient.Value,
                RequestConfiguration =
                {
                    UserAgent = $"MSLX/{PlatFormServices.GetFormattedVersion()} Downloader/{GetDownloaderVersion()} (.NET/{Environment.Version})"
                }
            };
        }

        public static string GetDownloaderVersion()
        {
            try
            {
                var assembly = typeof(DownloadService).Assembly;
                var infoVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (!string.IsNullOrEmpty(infoVersion))
                {
                    return infoVersion.Split('+')[0];
                }
                return assembly.GetName().Version?.ToString() ?? "Unknown";
            }
            catch { return "Unknown"; }
        }

        /// <summary>
        /// 从系统配置中读取下载线程数量（1-8，默认5）
        /// </summary>
        public static int GetConfiguredThreadCount()
        {
            try
            {
                var val = IConfigBase.Config.ReadConfigKey("downloadThreadCount");
                if (val != null)
                {
                    return Math.Clamp(Convert.ToInt32(val), 1, 8);
                }
            }
            catch { /* 回退默认咯 */ }
            return 5;
        }

        /// <summary>
        /// 异步下载文件
        /// </summary>
        /// <param name="url">下载地址</param>
        /// <param name="savePath">保存路径</param>
        /// <param name="onProgress">进度回调</param>
        /// <param name="progressIntervalMs">进度回调频率（毫秒），默认 1000ms</param>
        /// <returns>元组：(是否成功, 错误信息)</returns>
        public async Task<(bool Success, string ErrorMessage)> DownloadFileAsync(
            string url,
            string savePath,
            Action<double, string> onProgress = null,
            int progressIntervalMs = 1000,
            CancellationToken cancellationToken = default,
            int? parallelCountOverride = null,
            bool? parallelDownloadOverride = null)
        {
            await _fileConcurrencySemaphore.WaitAsync(cancellationToken);
            try
            {
                var downloader = new DownloadService(
                    CreateConfig(parallelCountOverride, parallelDownloadOverride));
                DateTime lastReportTime = DateTime.MinValue;
                long lastProgressTicks = DateTime.UtcNow.Ticks;
                long lastReceivedBytes = -1;
                object progressLock = new();

                downloader.DownloadProgressChanged += (s, e) =>
                {
                    lock (progressLock)
                    {
                        if (e.ReceivedBytesSize > lastReceivedBytes)
                        {
                            lastReceivedBytes = e.ReceivedBytesSize;
                            lastProgressTicks = DateTime.UtcNow.Ticks;
                        }
                    }

                    if ((DateTime.UtcNow - lastReportTime).TotalMilliseconds > progressIntervalMs || e.ProgressPercentage >= 100)
                    {
                        lastReportTime = DateTime.UtcNow;
                        string speed = ConvertBytesToReadable(e.AverageBytesPerSecondSpeed) + "/s";
                        onProgress?.Invoke(e.ProgressPercentage, speed);
                    }
                };

                var tcs = new TaskCompletionSource<(bool, string)>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                downloader.DownloadFileCompleted += (s, e) =>
                {
                    if (e.Cancelled) tcs.TrySetResult((false, "下载被取消"));
                    else if (e.Error != null) tcs.TrySetResult((false, e.Error.Message));
                    else tcs.TrySetResult((true, string.Empty));
                };

                using var cancellationRegistration = cancellationToken.Register(() =>
                {
                    tcs.TrySetResult((false, "下载已取消"));
                });
                using var watchdogCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                Task watchdogTask = Task.CompletedTask;
                if (_inactivityTimeout.HasValue)
                {
                    watchdogTask = Task.Run(async () =>
                    {
                        try
                        {
                            while (!watchdogCts.IsCancellationRequested)
                            {
                                await Task.Delay(TimeSpan.FromSeconds(5), watchdogCts.Token);
                                long progressTicks;
                                lock (progressLock) progressTicks = lastProgressTicks;
                                if (DateTime.UtcNow - new DateTime(progressTicks, DateTimeKind.Utc) <
                                    _inactivityTimeout.Value) continue;

                                tcs.TrySetResult((false,
                                    $"下载超过 {_inactivityTimeout.Value.TotalSeconds:0} 秒无进度"));
                                return;
                            }
                        }
                        catch (OperationCanceledException)
                        {
                        }
                    });
                }

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string dir = Path.GetDirectoryName(savePath);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    Console.WriteLine($"正在下载文件：{url}");
                    Task downloadTask = downloader.DownloadFileTaskAsync(url, savePath);
                    Task completedTask = await Task.WhenAny(downloadTask, tcs.Task);
                    if (completedTask == tcs.Task)
                    {
                        var result = await tcs.Task;
                        if (result.Item1)
                        {
                            await downloadTask;
                        }
                        else if (!downloadTask.IsCompleted)
                        {
                            try { await downloader.CancelTaskAsync(); } catch { }
                            await Task.WhenAny(downloadTask, Task.Delay(TimeSpan.FromSeconds(5)));
                        }
                        return result;
                    }

                    await downloadTask;
                    return tcs.Task.IsCompleted
                        ? await tcs.Task
                        : (true, string.Empty);
                }
                catch (Exception ex)
                {
                    return (false, ex.Message);
                }
                finally
                {
                    watchdogCts.Cancel();
                    try { await watchdogTask; } catch (OperationCanceledException) { }
                }
            }
            finally
            {
                _fileConcurrencySemaphore.Release();
            }
        }

        private string ConvertBytesToReadable(double bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            while (bytes >= 1024 && order < sizes.Length - 1)
            {
                order++;
                bytes /= 1024;
            }
            return $"{bytes:0.##} {sizes[order]}";
        }
    }
}
