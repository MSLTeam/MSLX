namespace MSLX.Daemon.Utils;

/// <summary>
/// 共享的静态日志工厂
/// </summary>
internal static class ApplicationLogging
{
    internal static ILoggerFactory LoggerFactory { get; set; } = new LoggerFactory();
    internal static ILogger CreateLogger<T>() => LoggerFactory.CreateLogger<T>();
    internal static ILogger CreateLogger(string categoryName) => LoggerFactory.CreateLogger(categoryName);
}