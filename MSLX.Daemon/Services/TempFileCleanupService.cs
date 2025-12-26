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