using MSLX.Daemon.Utils.ConfigUtils;

namespace MSLX.Daemon.Services;

public class TempFileCleanupService : BackgroundService
{
    private readonly ILogger<TempFileCleanupService> _logger;
    private readonly string _tempPath;

    public TempFileCleanupService(ILogger<TempFileCleanupService> logger)
    {
        _logger = logger;
        _tempPath = Path.Combine(IConfigBase.GetAppDataPath(), "Temp", "Uploads");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 上传凭证保存在内存中，Daemon 重启后不会恢复；对应的临时分片也必须一起清理，
        // 避免重启后遗留文件被误认为是可恢复的上传任务。
        try
        {
            if (Directory.Exists(_tempPath))
            {
                foreach (var file in Directory.GetFiles(_tempPath))
                {
                    try
                    {
                        System.IO.File.Delete(file);
                        _logger.LogInformation("Daemon 启动时清理上传临时文件: {File}", Path.GetFileName(file));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Daemon 启动时无法清理上传临时文件: {File}", Path.GetFileName(file));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Daemon 启动时清理上传临时文件出错");
        }

        // 创建一个 1小时 的定时器
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));

        try
        {
            do
            {
                try
                {
                    if (Directory.Exists(_tempPath))
                    {
                        var files = Directory.GetFiles(_tempPath);

                        foreach (var file in files)
                        {
                            try
                            {
                                var fi = new FileInfo(file);
                                if (DateTime.Now - fi.LastWriteTime > TimeSpan.FromHours(2))
                                {
                                    fi.Delete();
                                    _logger.LogInformation("已清理过期临时文件: {File}", fi.Name);
                                }
                            }
                            catch
                            {
                                // 忽略单个文件的删除错误
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "清理临时文件出错");
                }

            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // 正常退出
        }
    }
}
