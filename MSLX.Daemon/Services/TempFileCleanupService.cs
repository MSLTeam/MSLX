using MSLX.Daemon.Utils;

namespace MSLX.Daemon.Services;

public class TempFileCleanupService : BackgroundService
{
    private readonly ILogger<TempFileCleanupService> _logger;
    private readonly string _tempPath;

    public TempFileCleanupService(ILogger<TempFileCleanupService> logger)
    {
        _logger = logger;
        _tempPath = Path.Combine(ConfigServices.GetAppDataPath(), "DaemonData", "Temp", "Uploads");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (Directory.Exists(_tempPath))
                {
                    var files = Directory.GetFiles(_tempPath);
                    foreach (var file in files)
                    {
                        var fi = new FileInfo(file);
                        // 如果文件超过 2 小时没动静，就不要咯
                        if (DateTime.Now - fi.LastWriteTime > TimeSpan.FromHours(2))
                        {
                            try
                            {
                                fi.Delete();
                                _logger.LogInformation("已清理过期临时文件: {File}", fi.Name);
                            }
                            catch
                            {
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清理临时文件出错");
            }

            // 1h检查一次
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}