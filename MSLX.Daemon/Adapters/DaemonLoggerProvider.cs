using System.Diagnostics;
using System.Reflection;
using MSLX.SDK.Interfaces;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace MSLX.Daemon.Adapters;

public class DaemonLoggerProvider : IMSLXLogger
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ConcurrentDictionary<string, ILogger> _loggerCache = new();
    private readonly Assembly _sdkAssembly = typeof(MSLX.SDK.MSLX).Assembly;
    private readonly Assembly _adapterAssembly = typeof(DaemonLoggerProvider).Assembly;

    public DaemonLoggerProvider(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    private ILogger GetCallerLogger()
    {
        var stackTrace = new StackTrace();
        Assembly? callerAssembly = null;

        for (int i = 1; i < stackTrace.FrameCount; i++)
        {
            var assembly = stackTrace.GetFrame(i)?.GetMethod()?.DeclaringType?.Assembly;
            if (assembly != null && assembly != _sdkAssembly && assembly != _adapterAssembly)
            {
                callerAssembly = assembly;
                break;
            }
        }

        var categoryName = callerAssembly?.GetName().Name ?? "PluginSystem";

        var finalCategory = $"Plugin: {categoryName}";

        return _loggerCache.GetOrAdd(finalCategory, name => _loggerFactory.CreateLogger(name));
    }

    public void Info(string message) => GetCallerLogger().LogInformation(message);
    public void Warn(string message) => GetCallerLogger().LogWarning(message);
    public void Debug(string message) => GetCallerLogger().LogDebug(message);
    public void Error(string message, Exception? ex = null) => GetCallerLogger().LogError(ex, message);
}