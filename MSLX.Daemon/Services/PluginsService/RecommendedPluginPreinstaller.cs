using System.Net.Http.Json;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.Models;
using MSLX.SDK.Models.Plugins;

namespace MSLX.Daemon.Services.PluginsService;

internal sealed class RecommendedPluginPreinstaller(
    PluginPackageService packages,
    IHostApplicationLifetime lifetime,
    ILogger<RecommendedPluginPreinstaller> logger) : BackgroundService
{
    private const string MarketApi = "https://mslx-plugins-api.mslmc.net/api/plugins";
    private static readonly ParallelDownloader Downloader = new(maxTryAgainOnFailure: 2);

    public static bool IsFirstStartup(string dataPath)
    {
        try
        {
            var directory = new DirectoryInfo(dataPath);
            // Desktop 读取配置时可能已创建空的 Configs 目录
            return !directory.Exists || directory.EnumerateFileSystemInfos().All(entry =>
                entry is DirectoryInfo subdirectory &&
                (entry.Attributes & FileAttributes.ReparsePoint) == 0 &&
                !subdirectory.EnumerateFileSystemInfos().Any());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, lifetime.ApplicationStopping);
        var cancellationToken = stopping.Token;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var startupRegistration = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        string? stagingDirectory = null;

        try
        {
            await started.Task.WaitAsync(cancellationToken);
            logger.LogInformation("[插件预装] 首次启动，正在预安装推荐插件···");
            var recommendations = await GetRecommendationsAsync(cancellationToken);
            logger.LogInformation("[插件预装] 获取到 {Count} 个推荐插件", recommendations.Count);
            if (recommendations.Count == 0) return;

            stagingDirectory = Path.Combine(IConfigBase.GetAppDataPath(), "Temp", "PluginPreinstall", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingDirectory);
            var downloaded = new List<(string AppId, string Path)>();
            foreach (var plugin in recommendations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var path = Path.Combine(stagingDirectory, $"{Guid.NewGuid():N}.dll");
                    await DownloadAsync(plugin.AppId, path, cancellationToken);
                    downloaded.Add((plugin.AppId, path));
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "[插件预装] 下载插件失败: {AppId}", plugin.AppId);
                }
            }

            logger.LogInformation("[插件预装] 下载完成，正在启用 {Count} 个插件", downloaded.Count);
            var enabledCount = 0;
            foreach (var (appId, path) in downloaded)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    // 插件中心的 AppId 可能与 DLL 声明的插件 ID 不同。
                    var installed = await packages.InstallAsync(path, new UploadPluginRequest
                    {
                        Enabled = true,
                        Overwrite = false
                    }, cancellationToken);
                    enabledCount++;
                    logger.LogInformation("[插件预装] 已安装并启用 {PluginId} v{Version}", installed.Id, installed.Version);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "[插件预装] 安装或启用插件失败: {AppId}", appId);
                }
            }

            logger.LogInformation("[插件预装] 预装结束，{Total} 个推荐插件中成功启用 {Enabled} 个", recommendations.Count, enabledCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("[插件预装] Daemon 正在停止，已取消预装");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[插件预装] 预装流程中断");
        }
        finally
        {
            if (stagingDirectory != null && Directory.Exists(stagingDirectory))
            {
                try { Directory.Delete(stagingDirectory, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(ex, "[插件预装] 清理临时下载目录失败: {Path}", stagingDirectory);
                }
            }
        }
    }

    private static async Task<List<MarketPlugin>> GetRecommendationsAsync(CancellationToken cancellationToken)
    {
        var recommendations = new List<MarketPlugin>();
        for (var page = 1; ; page++)
        {
            var (plugins, total) = await GetPageAsync<MarketPlugin>(
                $"list?page={page}&size=1027&categoryId=6", cancellationToken);
            recommendations.AddRange(plugins);
            if (plugins.Count == 0 || recommendations.Count >= total)
                return recommendations.DistinctBy(plugin => plugin.AppId, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    private async Task DownloadAsync(string id, string path, CancellationToken cancellationToken)
    {
        PluginPackageService.ValidateId(id);
        var (versions, _) = await GetPageAsync<MarketVersion>(
            $"versions/list/{Uri.EscapeDataString(id)}?page=1&size=1", cancellationToken);
        var version = versions.FirstOrDefault() ?? throw new InvalidDataException("插件中心没有可下载版本");
        if (!Uri.TryCreate(version.DownloadLink, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("插件中心返回了无效的下载地址");

        logger.LogInformation("[插件预装] 正在下载 {PluginId} v{Version}", id, version.VersionName);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        var result = await Downloader.DownloadFileAsync(url.AbsoluteUri, path, cancellationToken: timeout.Token, strictSsrfCheck: true);
        cancellationToken.ThrowIfCancellationRequested();
        if (timeout.IsCancellationRequested) throw new TimeoutException("插件下载超时");
        if (!result.Success) throw new IOException(result.ErrorMessage);
        logger.LogInformation("[插件预装] 已下载 {PluginId} v{Version}", id, version.VersionName);
    }

    private static async Task<(List<T> Items, int Total)> GetPageAsync<T>(string endpoint, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var response = await HttpService.SharedClient.GetFromJsonAsync<ApiResponse<MarketPage<T>>>(
            $"{MarketApi}/{endpoint}", timeout.Token);
        if (response is not { Code: 200, Data.List: { } items })
            throw new InvalidDataException($"插件中心返回无效列表: {response?.Code} {response?.Message}");
        return (items, response.Data.Total);
    }

    private sealed record MarketPage<T>(List<T>? List, int Total);
    private sealed record MarketPlugin(string AppId);
    private sealed record MarketVersion(string VersionName, string DownloadLink);
}
