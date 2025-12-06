using Downloader;
using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MSLX.Daemon.Utils
{
    public class ParallelDownloader
    {
        private readonly DownloadConfiguration _config;
        private readonly SemaphoreSlim _fileConcurrencySemaphore; // 控制并发

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="parallelCount">单个文件的并行分块/线程数 (默认8)</param>
        /// <param name="maxSimultaneousFiles">同时允许下载的文件数量 (默认3)</param>
        public ParallelDownloader(int parallelCount = 8, int maxSimultaneousFiles = 3)
        {
            // 初始化信号量
            _fileConcurrencySemaphore = new SemaphoreSlim(maxSimultaneousFiles);

            // 配置并行下载参数
            _config = new DownloadConfiguration
            {
                ChunkCount = parallelCount,
                ParallelDownload = true,
                ParallelCount = parallelCount,
                MaxTryAgainOnFailure = 5,
                Timeout = 5000,
                ReserveStorageSpaceBeforeStartingDownload = true,
                RequestConfiguration =
                {
                    UserAgent = "MSLTeam-MSLX/1.0 (Downloader)"
                }
            };
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
            // 请求信号量 控制并发
            await _fileConcurrencySemaphore.WaitAsync();

            try
            {
                var downloader = new DownloadService(_config);

                // 用于记录上次汇报的时间
                DateTime lastReportTime = DateTime.MinValue;

                downloader.DownloadProgressChanged += (s, e) =>
                {
                    // 检查时间间隔 或 进度完成
                    if ((DateTime.UtcNow - lastReportTime).TotalMilliseconds > progressIntervalMs || e.ProgressPercentage >= 100)
                    {
                        lastReportTime = DateTime.UtcNow;
                        string speed = ConvertBytesToReadable(e.AverageBytesPerSecondSpeed) + "/s";
                        onProgress?.Invoke(e.ProgressPercentage, speed);
                    }
                };

                // 修改 TCS 类型以支持返回错误信息
                var tcs = new TaskCompletionSource<(bool, string)>();

                downloader.DownloadFileCompleted += (s, e) =>
                {
                    if (e.Cancelled) 
                    {
                        tcs.TrySetResult((false, "下载被取消"));
                    }
                    else if (e.Error != null) 
                    {
                        // 捕获具体异常信息
                        tcs.TrySetResult((false, e.Error.Message));
                    }
                    else 
                    {
                        tcs.TrySetResult((true, string.Empty));
                    }
                };

                try
                {
                    string dir = Path.GetDirectoryName(savePath);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    Console.WriteLine($"正在下载文件：{url}");
                    await downloader.DownloadFileTaskAsync(url, savePath);
                    
                    // 等待 TCS 结果
                    return await tcs.Task;
                }
                catch (Exception ex)
                {
                    // 捕获启动时的异常（如路径非法等）
                    return (false, ex.Message);
                }
            }
            finally
            {
                // 释放信号量
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
                bytes = bytes / 1024;
            }
            return $"{bytes:0.##} {sizes[order]}";
        }
    }
}