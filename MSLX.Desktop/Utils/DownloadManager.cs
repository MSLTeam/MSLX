using CommunityToolkit.Mvvm.ComponentModel;
using Downloader;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace MSLX.Desktop.Utils
{

    #region 下载进度信息类 (可绑定)
    public partial class DownloadProgressInfo : ObservableObject
    {
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(EstimatedTimeRemaining))]
        private long _receivedBytes;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(EstimatedTimeRemaining))]
        private long _totalBytes;

        [ObservableProperty]
        private double _progressPercentage;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(EstimatedTimeRemaining))]
        private double _bytesPerSecond;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ElapsedTime))]
        private DateTime _startTime;

        public TimeSpan ElapsedTime => DateTime.Now - StartTime;

        public TimeSpan EstimatedTimeRemaining
        {
            get
            {
                if (BytesPerSecond <= 0 || ReceivedBytes >= TotalBytes)
                    return TimeSpan.Zero;

                long remainingBytes = TotalBytes - ReceivedBytes;
                // 避免除以0
                if (remainingBytes <= 0) return TimeSpan.Zero;

                double secondsRemaining = remainingBytes / BytesPerSecond;
                return TimeSpan.FromSeconds(secondsRemaining);
            }
        }
    }
    #endregion

    #region 下载项信息类 (可绑定)
    public partial class DownloadItem : ObservableObject
    {
        public required string ItemId { get; set; }
        public required string GroupId { get; set; }
        public required string Url { get; set; }
        public required string DownloadPath { get; set; }

        [ObservableProperty]
        private string? _filename;
        public string? ExpectedSha256 { get; set; }
        public bool EnableParallel { get; set; }
        public DownloadUAMode UAMode { get; set; }
        public int RetryCount { get; set; }

        [ObservableProperty]
        private DownloadStatus _status;

        [ObservableProperty]
        private string? _errorMessage;

        // 注意：这里初始化一个实例，后续只更新属性，不替换对象
        [ObservableProperty]
        private DownloadProgressInfo _progress = new DownloadProgressInfo();

        // 用于单个item的取消令牌
        internal CancellationTokenSource CancellationTokenSource { get; set; } = new();
    }
    #endregion

    #region 下载组信息类 (可绑定)
    public partial class DownloadGroup : ObservableObject
    {
        public required string GroupId { get; set; }

        [ObservableProperty]
        private DownloadGroupStatus _status;

        public int MaxConcurrentDownloads { get; set; }

        // 如果 UI 需要显示组内的 ID 列表，建议改为 ObservableCollection<string>
        // 但如果只是内部逻辑使用，List 也可以，这里保持 List
        public List<string> Items { get; set; } = new List<string>();

        public TaskCompletionSource<bool> CompletionTask { get; set; } = new TaskCompletionSource<bool>();
        public bool IsTempGroup { get; set; }

        // 用于组级别的取消令牌
        internal CancellationTokenSource CancellationTokenSource { get; set; } = new CancellationTokenSource();
    }
    #endregion

    #region 其他下载属性
    public enum DownloadGroupStatus { Ready, InProgress, Cancelling, Completed, CompletedWithErrors }
    public enum DownloadStatus { Pending, InProgress, Paused, Cancelling, Cancelled, Completed, Failed, Retrying }
    public enum DownloadUAMode { None = 0, MSL = 1, Browser = 2 }
    #endregion

    #region DownloadManager服务
    public class DownloadManager
    {
        #region 事件定义
        // 在 Avalonia/MVVM 中，由于绑定了 ObservableObject，通常不需要这些事件来更新 UI。
        // 但可以保留这些事件用于日志记录或特定逻辑处理。
        public delegate void DownloadItemProgressChangedEventHandler(string groupId, string itemId, DownloadProgressInfo progressInfo);
        public delegate void DownloadItemCompletedEventHandler(string groupId, string itemId, bool success, Exception? error = null);
        public delegate void DownloadGroupCompletedEventHandler(string groupId, bool allSuccess);

        public event DownloadItemProgressChangedEventHandler? DownloadItemProgressChanged;
        public event DownloadItemCompletedEventHandler? DownloadItemCompleted;
        public event DownloadGroupCompletedEventHandler? DownloadGroupCompleted;
        #endregion

        #region 私有字段
        private readonly ConcurrentDictionary<string, DownloadGroup> _downloadGroups = new ConcurrentDictionary<string, DownloadGroup>();
        private readonly ConcurrentDictionary<string, DownloadItem> _downloadItems = new ConcurrentDictionary<string, DownloadItem>();
        private readonly ConcurrentDictionary<string, DownloadService> _downloaders = new ConcurrentDictionary<string, DownloadService>();

        // 用于备用下载的 HttpClient 实例（复用连接）
        private static readonly HttpClient _httpClient = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        })
        {
            Timeout = TimeSpan.FromMinutes(30)
        };
        #endregion

        #region 公共属性
        public static int DefaultConcurrentDownloads { get; set; } = 4;
        public const string DefaultGroupId = "default";
        #endregion

        #region 单例模式
        private static readonly Lazy<DownloadManager> _instance = new Lazy<DownloadManager>(() => new DownloadManager());
        public static DownloadManager Instance => _instance.Value;

        private DownloadManager()
        {
            // 初始化默认下载组
            CreateDownloadGroup(DefaultGroupId, isTempGroup: false, maxConcurrentDownloads: DefaultConcurrentDownloads);
        }
        #endregion

        #region 公共方法
        public string CreateDownloadGroup(string? groupId = null, bool isTempGroup = false, int maxConcurrentDownloads = default)
        {
            if (maxConcurrentDownloads == default)
                maxConcurrentDownloads = DefaultConcurrentDownloads;
            groupId = groupId ?? Guid.NewGuid().ToString();
            var group = new DownloadGroup
            {
                GroupId = groupId,
                MaxConcurrentDownloads = maxConcurrentDownloads,
                Status = DownloadGroupStatus.Ready,
                IsTempGroup = isTempGroup
            };
            _downloadGroups[groupId] = group;
            return groupId;
        }

        public string AddDownloadItem(string groupId, string url, string downloadPath, string? filename,
                                      string expectedSha256 = "", string? itemId = null, bool enableParallel = true,
                                      DownloadUAMode uaMode = DownloadUAMode.MSL, int retryCount = 1)
        {
            if (!_downloadGroups.ContainsKey(groupId))
                throw new ArgumentException($"Download group '{groupId}' does not exist");

            itemId = itemId ?? Guid.NewGuid().ToString();
            Directory.CreateDirectory(downloadPath);

            var item = new DownloadItem
            {
                ItemId = itemId,
                GroupId = groupId,
                Url = url,
                DownloadPath = downloadPath,
                Filename = filename,
                ExpectedSha256 = expectedSha256,
                EnableParallel = enableParallel,
                UAMode = uaMode,
                Status = DownloadStatus.Pending,
                RetryCount = retryCount,
                CancellationTokenSource = new CancellationTokenSource()
                // Progress 已在构造函数中初始化
            };

            _downloadItems[itemId] = item;
            _downloadGroups[groupId].Items.Add(itemId);

            return itemId;
        }

        public void StartDownloadGroup(string groupId)
        {
            if (!_downloadGroups.TryGetValue(groupId, out var group))
                throw new ArgumentException($"Download group '{groupId}' does not exist");

            if (group.Status == DownloadGroupStatus.InProgress)
                return;

            group.Status = DownloadGroupStatus.InProgress;
            Task.Run(() => ProcessDownloadGroup(groupId));
        }

        /// <summary>
        /// 启动单个下载项（不需要通过组）
        /// </summary>
        public void StartDownloadItem(string itemId)
        {
            if (!_downloadItems.TryGetValue(itemId, out var item))
                throw new ArgumentException($"Download item '{itemId}' does not exist");

            if (item.Status == DownloadStatus.InProgress || item.Status == DownloadStatus.Completed)
                return;

            // 重置取消令牌（如果之前被取消过）
            if (item.CancellationTokenSource?.IsCancellationRequested == true)
            {
                item.CancellationTokenSource?.Dispose();
                item.CancellationTokenSource = new CancellationTokenSource();
            }

            item.Status = DownloadStatus.Pending;
            Task.Run(() => DownloadItemAsync(itemId));
        }

        public Task<bool> WaitForItemCompletionAsync(string itemId)
        {
            if (!_downloadItems.TryGetValue(itemId, out var item))
                throw new ArgumentException($"Download item '{itemId}' does not exist");
            var tcs = new TaskCompletionSource<bool>();
            void Handler(string groupId, string completedItemId, bool success, Exception? error)
            {
                if (completedItemId == itemId)
                {
                    DownloadItemCompleted -= Handler;
                    tcs.TrySetResult(success);
                }
            }
            DownloadItemCompleted += Handler;
            return tcs.Task;
        }

        public Task<bool> WaitForGroupCompletionAsync(string groupId)
        {
            if (!_downloadGroups.TryGetValue(groupId, out var group))
                throw new ArgumentException($"Download group '{groupId}' does not exist");

            var task = group.CompletionTask.Task;
            // 注意：如果在等待前任务已经完成且被移除，这里可能会有问题
            return task.ContinueWith(t =>
            {
                if (group.IsTempGroup) RemoveDownloadGroup(groupId);
                return t.Result;
            });
        }

        public void CancelDownloadGroup(string groupId)
        {
            if (!_downloadGroups.TryGetValue(groupId, out var group)) return;

            group.Status = DownloadGroupStatus.Cancelling;
            group.CancellationTokenSource?.Cancel();

            foreach (var itemId in group.Items)
            {
                if (_downloadItems.TryGetValue(itemId, out var item) &&
                    (item.Status == DownloadStatus.InProgress || item.Status == DownloadStatus.Pending))
                {
                    CancelDownloadItem(itemId);
                }
            }
        }

        public void CancelDownloadItem(string itemId)
        {
            if (!_downloadItems.TryGetValue(itemId, out var item)) return;

            item.Status = DownloadStatus.Cancelling;
            item.CancellationTokenSource?.Cancel();

            if (_downloaders.TryGetValue(itemId, out var downloader))
            {
                downloader.CancelAsync();
            }
        }

        public void PauseDownloadItem(string itemId)
        {
            if (!_downloadItems.TryGetValue(itemId, out var item) || item.Status != DownloadStatus.InProgress) return;

            if (_downloaders.TryGetValue(itemId, out var downloader))
            {
                downloader.Pause();
                item.Status = DownloadStatus.Paused;
            }
        }

        public void ResumeDownloadItem(string itemId)
        {
            if (!_downloadItems.TryGetValue(itemId, out var item) || item.Status != DownloadStatus.Paused) return;

            if (_downloaders.TryGetValue(itemId, out var downloader))
            {
                downloader.Resume();
                item.Status = DownloadStatus.InProgress;
            }
        }

        public IEnumerable<DownloadGroup> GetAllGroups() => _downloadGroups.Values.ToList();

        public IEnumerable<DownloadItem> GetGroupItems(string groupId)
        {
            if (!_downloadGroups.TryGetValue(groupId, out var group))
                return Enumerable.Empty<DownloadItem>();

            return group.Items
                .Where(id => _downloadItems.ContainsKey(id))
                .Select(id => _downloadItems[id])
                .ToList();
        }

        public DownloadItem? GetDownloadItem(string itemId) =>
            _downloadItems.TryGetValue(itemId, out var item) ? item : null;

        public void RemoveDownloadGroup(string groupId)
        {
            // 不允许删除默认组
            if (groupId == DefaultGroupId)
                throw new InvalidOperationException("Cannot remove the default download group");

            if (!_downloadGroups.TryGetValue(groupId, out var group)) return;

            if (group.Status == DownloadGroupStatus.InProgress)
                CancelDownloadGroup(groupId);

            foreach (var itemId in group.Items.ToList())
            {
                _downloadItems.TryRemove(itemId, out _);
                _downloaders.TryRemove(itemId, out _);
            }

            group.CancellationTokenSource?.Dispose();
            _downloadGroups.TryRemove(groupId, out _);
        }
        #endregion

        #region 快速调用

        /// <summary>
        /// 使用默认组快速下载文件
        /// </summary>
        /// <returns>返回下载项ID和下载项对象</returns>
        public static (string ItemId, DownloadItem? Item) DownloadWithDefaultGroup(
            string url,
            string path,
            string? filename,
            string expectedSha256 = "",
            bool enableParallel = true,
            DownloadUAMode uaMode = DownloadUAMode.MSL,
            int retryCount = 1,
            bool autoStart = true)
        {
            // 添加任务到默认组
            string itemId = Instance.AddDownloadItem(
                DefaultGroupId,
                url,
                path,
                filename,
                expectedSha256,
                enableParallel: enableParallel,
                uaMode: uaMode,
                retryCount: retryCount
            );

            var item = Instance.GetDownloadItem(itemId);

            // 自动启动下载（可选）
            if (autoStart)
            {
                Instance.StartDownloadItem(itemId);
            }

            return (itemId, item);
        }

        /// <summary>
        /// 快速下载文件并等待完成
        /// </summary>
        public static async Task<bool> DownloadWDAndWaitAsync(
            string url,
            string path,
            string? filename,
            string expectedSha256 = "",
            bool enableParallel = true,
            DownloadUAMode uaMode = DownloadUAMode.MSL,
            int retryCount = 1)
        {
            var (itemId, item) = DownloadWithDefaultGroup(url, path, filename, expectedSha256, enableParallel, uaMode, retryCount, autoStart: true);

            if (item == null) return false;

            await Instance.WaitForItemCompletionAsync(itemId);

            return item.Status == DownloadStatus.Completed;
        }

        #endregion

        #region 私有逻辑
        private async void ProcessDownloadGroup(string groupId)
        {
            if (!_downloadGroups.TryGetValue(groupId, out var group)) return;

            var pendingItems = group.Items
                .Where(id => _downloadItems.ContainsKey(id) && _downloadItems[id].Status == DownloadStatus.Pending)
                .ToList();

            var downloadTasks = new List<Task>();
            using var semaphore = new SemaphoreSlim(group.MaxConcurrentDownloads);

            foreach (var itemId in pendingItems)
            {
                await semaphore.WaitAsync(group.CancellationTokenSource.Token);
                var downloadTask = Task.Run(async () =>
                {
                    try { await DownloadItemAsync(itemId); }
                    finally { semaphore.Release(); }
                }, group.CancellationTokenSource.Token);
                downloadTasks.Add(downloadTask);
            }

            await Task.WhenAll(downloadTasks);
            CompleteDownloadGroup(groupId);
        }

        private void CompleteDownloadGroup(string groupId)
        {
            if (!_downloadGroups.TryGetValue(groupId, out var group)) return;

            bool allSuccess = group.Items.Select(id => _downloadItems.TryGetValue(id, out var i) ? i : null)
                                         .All(i => i != null && i.Status == DownloadStatus.Completed);

            group.Status = allSuccess ? DownloadGroupStatus.Completed : DownloadGroupStatus.CompletedWithErrors;
            group.CompletionTask.TrySetResult(allSuccess);
            DownloadGroupCompleted?.Invoke(groupId, allSuccess);
        }

        private async Task DownloadItemAsync(string itemId)
        {
            if (!_downloadItems.TryGetValue(itemId, out var item)) return;

            if (string.IsNullOrEmpty(item.Filename))
            {
                item.Filename = await GetActualFileNameAsync(item.Url);
            }

            string fullPath = Path.Combine(item.DownloadPath, item.Filename);

            // 检查已存在文件
            if (File.Exists(fullPath) && !string.IsNullOrEmpty(item.ExpectedSha256) && VerifyFileSHA256(fullPath, item.ExpectedSha256))
            {
                item.Status = DownloadStatus.Completed;
                CompleteDownloadItem(item, true, null);
                return;
            }

            item.Status = DownloadStatus.InProgress;
            bool success = false;
            Exception? error = null;

            try
            {
                var downloadOpt = new DownloadConfiguration
                {
                    ParallelDownload = item.EnableParallel,
                    ChunkCount = 8 // 默认分块数，或从配置读取
                };
                downloadOpt.RequestConfiguration.UserAgent = GetUserAgent(item.UAMode);

                using var downloader = new DownloadService(downloadOpt);
                _downloaders[itemId] = downloader;

                downloader.DownloadStarted += (s, e) =>
                {
                    // 在 UI 线程安全更新 (ObservableObject 通常是线程安全的，但在 Avalonia 中最好注意)
                    item.Progress.TotalBytes = e.TotalBytesToReceive;
                    item.Progress.StartTime = DateTime.Now;
                };

                downloader.DownloadProgressChanged += (s, e) => OnDownloadProgressChanged(itemId, e);

                var tcs = new TaskCompletionSource<bool>();
                downloader.DownloadFileCompleted += (s, e) => OnDownloadFileCompleted(itemId, e, tcs);

                await downloader.DownloadFileTaskAsync(item.Url, fullPath);
                success = await tcs.Task;
            }
            catch (Exception ex)
            {
                success = false;
                error = ex;
                if (!IsCancellingOrCancelled(item.Status))
                {
                    try { success = await FallbackDownloadAsync(item); }
                    catch (Exception fbEx) { error = fbEx; }
                }
            }
            finally
            {
                CompleteDownloadItem(item, success, error);
                if (!success && !IsCancellingOrCancelled(item.Status) && item.RetryCount > 0)
                {
                    await RetryDownloadAsync(item);
                }
            }
        }

        private void OnDownloadProgressChanged(string itemId, Downloader.DownloadProgressChangedEventArgs e)
        {
            if (!_downloadItems.TryGetValue(itemId, out var item)) return;

            item.Progress.ReceivedBytes = e.ReceivedBytesSize;
            item.Progress.TotalBytes = e.TotalBytesToReceive;
            item.Progress.ProgressPercentage = e.ProgressPercentage;
            item.Progress.BytesPerSecond = e.BytesPerSecondSpeed;

            DownloadItemProgressChanged?.Invoke(item.GroupId, itemId, item.Progress);
        }

        private void OnDownloadFileCompleted(string itemId, System.ComponentModel.AsyncCompletedEventArgs e, TaskCompletionSource<bool> tcs)
        {
            if (!_downloadItems.TryGetValue(itemId, out var item))
            {
                tcs.TrySetResult(false);
                return;
            }

            string filePath = Path.Combine(item.DownloadPath, item.Filename ?? "download_file");

            if (e.Cancelled)
            {
                item.Status = DownloadStatus.Cancelled;
                TryDeleteFile(filePath);
                tcs.TrySetResult(false);
                return;
            }

            if (e.Error != null)
            {
                item.ErrorMessage = e.Error.Message;
                tcs.TrySetResult(false);
                return;
            }

            if (!File.Exists(filePath))
            {
                item.ErrorMessage = "File not found";
                tcs.TrySetResult(false);
                return;
            }

            if (!string.IsNullOrEmpty(item.ExpectedSha256) && !VerifyFileSHA256(filePath, item.ExpectedSha256))
            {
                item.ErrorMessage = "SHA256 verification failed";
                TryDeleteFile(filePath);
                tcs.TrySetResult(false);
                return;
            }

            tcs.TrySetResult(true);
        }

        /// <summary>
        /// 备用下载方法 - 使用 HttpClient 替代过时的 HttpWebRequest
        /// </summary>
        private async Task<bool> FallbackDownloadAsync(DownloadItem item)
        {
            string filePath = Path.Combine(item.DownloadPath, item.Filename ?? await GetActualFileNameAsync(item.Url));
            FileStream? fileStream = null;

            try
            {
                // 创建 HTTP 请求消息
                using var request = new HttpRequestMessage(HttpMethod.Get, item.Url);

                // 设置 User Agent
                var userAgent = GetUserAgent(item.UAMode);
                if (!string.IsNullOrEmpty(userAgent))
                {
                    request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
                }

                // 发送请求并获取响应（使用 HttpCompletionOption.ResponseHeadersRead 以支持流式下载）
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, item.CancellationTokenSource.Token);
                response.EnsureSuccessStatusCode();

                long totalBytes = response.Content.Headers.ContentLength ?? 0;
                item.Progress.TotalBytes = totalBytes;
                item.Progress.StartTime = DateTime.Now;

                // 创建文件流
                fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, useAsync: true);

                // 获取内容流
                using var contentStream = await response.Content.ReadAsStreamAsync();

                byte[] buffer = new byte[8192];
                int bytesRead;
                long totalDownloaded = 0;
                long lastNotifiedBytes = 0;
                DateTime lastTime = DateTime.Now;

                // 读取并写入数据
                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, item.CancellationTokenSource.Token)) > 0)
                {
                    // 检查是否取消
                    if (item.CancellationTokenSource.Token.IsCancellationRequested || IsCancellingOrCancelled(item.Status))
                    {
                        await fileStream.DisposeAsync();
                        TryDeleteFile(filePath);
                        return false;
                    }

                    await fileStream.WriteAsync(buffer, 0, bytesRead, item.CancellationTokenSource.Token);
                    totalDownloaded += bytesRead;

                    var now = DateTime.Now;
                    // 每500ms更新一次进度
                    if ((now - lastTime).TotalMilliseconds > 500)
                    {
                        double speed = (totalDownloaded - lastNotifiedBytes) / (now - lastTime).TotalSeconds;
                        item.Progress.ReceivedBytes = totalDownloaded;
                        item.Progress.ProgressPercentage = totalBytes > 0 ? (double)totalDownloaded * 100 / totalBytes : 0;
                        item.Progress.BytesPerSecond = speed;

                        DownloadItemProgressChanged?.Invoke(item.GroupId, item.ItemId, item.Progress);

                        lastTime = now;
                        lastNotifiedBytes = totalDownloaded;
                    }
                }

                // 确保文件完全写入
                await fileStream.FlushAsync(item.CancellationTokenSource.Token);
                await fileStream.DisposeAsync();
                fileStream = null;

                // 验证文件完整性
                if (totalBytes > 0 && totalDownloaded != totalBytes)
                {
                    item.ErrorMessage = $"Download incomplete: {totalDownloaded}/{totalBytes} bytes";
                    TryDeleteFile(filePath);
                    return false;
                }

                // 验证 SHA256
                if (!string.IsNullOrEmpty(item.ExpectedSha256) && !VerifyFileSHA256(filePath, item.ExpectedSha256))
                {
                    item.ErrorMessage = "SHA256 verification failed";
                    TryDeleteFile(filePath);
                    return false;
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                item.ErrorMessage = "Download cancelled";
                TryDeleteFile(filePath);
                return false;
            }
            catch (HttpRequestException ex)
            {
                item.ErrorMessage = $"HTTP error: {ex.Message}";
                TryDeleteFile(filePath);
                return false;
            }
            catch (Exception ex)
            {
                item.ErrorMessage = ex.Message;
                TryDeleteFile(filePath);
                return false;
            }
            finally
            {
                // 确保文件流被关闭
                if (fileStream != null)
                {
                    await fileStream.DisposeAsync();
                }
            }
        }

        private void CompleteDownloadItem(DownloadItem item, bool success, Exception? error)
        {
            if (IsCancellingOrCancelled(item.Status))
            {
                item.Status = DownloadStatus.Cancelled;
            }
            else
            {
                item.Status = success ? DownloadStatus.Completed : DownloadStatus.Failed;
                if (!success && error != null) item.ErrorMessage = error.Message;
            }

            DownloadItemCompleted?.Invoke(item.GroupId, item.ItemId, success, error);
            if (_downloaders.TryRemove(item.ItemId, out var d)) d.Dispose();
        }

        private async Task RetryDownloadAsync(DownloadItem item)
        {
            item.Status = DownloadStatus.Retrying;
            item.RetryCount--;
            await Task.Delay(1500);
            TryDeleteFile(Path.Combine(item.DownloadPath, item.Filename ?? "download_file"));
            await Task.Delay(1500);

            // 重置取消令牌
            item.CancellationTokenSource?.Dispose();
            item.CancellationTokenSource = new CancellationTokenSource();

            await DownloadItemAsync(item.ItemId);
        }

        private bool IsCancellingOrCancelled(DownloadStatus status) =>
            status == DownloadStatus.Cancelling || status == DownloadStatus.Cancelled;

        private bool TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); return true; } catch { return false; }
        }

        private async Task<string> GetActualFileNameAsync(string url)
        {
            try
            {
                // 使用 HttpClient 发送 HEAD 请求，只获取头部不下载内容，速度极快
                using var client = new System.Net.Http.HttpClient();
                using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Head, url);
                using var response = await client.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    // 1. 尝试从 Content-Disposition 头获取 (例如: attachment; filename="realname.zip")
                    var contentDisposition = response.Content.Headers.ContentDisposition;
                    if (contentDisposition != null && !string.IsNullOrEmpty(contentDisposition.FileName))
                    {
                        return contentDisposition.FileName.Trim('"'); // 去除引号
                    }
                }
            }
            catch { }
            // 2. 如果头部没有，或者请求失败，则从 URL 路径解析
            return GetFileNameFromUrl(url);
        }

        private string GetFileNameFromUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                string fileName = Path.GetFileName(uri.LocalPath);
                return string.IsNullOrEmpty(fileName) ? "download_file_" + Guid.NewGuid().ToString().Substring(0, 8) : fileName;
            }
            catch
            {
                return "unknown_" + DateTime.Now.Ticks;
            }
        }

        private string? GetUserAgent(DownloadUAMode mode) => mode switch
        {
            DownloadUAMode.MSL => "MSLTeam-MSL/1.0 (Downloader)",
            DownloadUAMode.Browser => "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36",
            _ => null
        };

        private bool VerifyFileSHA256(string filePath, string expectedHash)
        {
            try
            {
                using var stream = File.OpenRead(filePath);
                using var sha = SHA256.Create();
                byte[] hash = sha.ComputeHash(stream);
                string calculated = BitConverter.ToString(hash).Replace("-", string.Empty);
                return string.Equals(calculated, expectedHash, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
        #endregion
    }
    #endregion
}