using MSLX.SDK.Interfaces;
using MSLX.Daemon.Utils;

namespace MSLX.Daemon.Adapters;

public class DaemonDownloadProvider : IDownloadService
{
    private readonly ParallelDownloader _downloader;

    public DaemonDownloadProvider()
    {
        _downloader = new ParallelDownloader(); 
    }

    public Task<(bool Success, string ErrorMessage)> DownloadFileAsync(
        string url, 
        string savePath, 
        Action<double, string>? onProgress = null, 
        int progressIntervalMs = 1000)
    {
        return _downloader.DownloadFileAsync(url, savePath, onProgress, progressIntervalMs);
    }
}