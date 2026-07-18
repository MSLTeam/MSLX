using Downloader;
using System.Reflection;

namespace MSLX.Daemon.Utils
{
    public class ParallelDownloader
    {
        private readonly DownloadConfiguration _config;
        private readonly SemaphoreSlim _fileConcurrencySemaphore;

        public ParallelDownloader(int parallelCount = 8, int maxSimultaneousFiles = 3)
        {
            _fileConcurrencySemaphore = new SemaphoreSlim(maxSimultaneousFiles);
            _config = new DownloadConfiguration
            {
                ChunkCount = parallelCount,
                ParallelDownload = true,
                ParallelCount = parallelCount,
                MaxTryAgainOnFailure = 5,
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
            int progressIntervalMs = 1000)
        {
            await _fileConcurrencySemaphore.WaitAsync();
            try
            {
                var downloader = new DownloadService(_config);
                DateTime lastReportTime = DateTime.MinValue;

                downloader.DownloadProgressChanged += (s, e) =>
                {
                    if ((DateTime.UtcNow - lastReportTime).TotalMilliseconds > progressIntervalMs || e.ProgressPercentage >= 100)
                    {
                        lastReportTime = DateTime.UtcNow;
                        string speed = ConvertBytesToReadable(e.AverageBytesPerSecondSpeed) + "/s";
                        onProgress?.Invoke(e.ProgressPercentage, speed);
                    }
                };

                var tcs = new TaskCompletionSource<(bool, string)>();
                downloader.DownloadFileCompleted += (s, e) =>
                {
                    if (e.Cancelled) tcs.TrySetResult((false, "下载被取消"));
                    else if (e.Error != null) tcs.TrySetResult((false, e.Error.Message));
                    else tcs.TrySetResult((true, string.Empty));
                };

                try
                {
                    string dir = Path.GetDirectoryName(savePath);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    Console.WriteLine($"正在下载文件：{url}");
                    await downloader.DownloadFileTaskAsync(url, savePath);
                    return await tcs.Task;
                }
                catch (Exception ex)
                {
                    return (false, ex.Message);
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