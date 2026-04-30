using MSLX.SDK.Interfaces;
using Microsoft.Extensions.Logging;

namespace MSLX.Daemon.Adapters;

public class DaemonLoggerProvider : IMSLXLogger
{
    private readonly ILogger _logger;

    public DaemonLoggerProvider(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger("PluginSystem");
    }

    public void Info(string message) => _logger.LogInformation(message);
    public void Warn(string message) => _logger.LogWarning(message);
    public void Debug(string message) => _logger.LogDebug(message);
    public void Error(string message, Exception? ex = null) => _logger.LogError(ex, message);
}