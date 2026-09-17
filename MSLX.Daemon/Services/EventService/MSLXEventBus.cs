using System.Reflection;
using Microsoft.Extensions.Logging;
using MSLX.SDK.Events;
using MSLX.SDK.Interfaces;

namespace MSLX.Daemon.Services.EventService;

/// <summary>
/// MSLX 全局事件总线实现类
/// 提供安全的事件发布、异常隔离与插件程序集事件自动注销
/// </summary>
public class MSLXEventBus : IMSLXEvents
{
    private readonly ILogger<MSLXEventBus> _logger;
    private readonly object _lock = new();

    public MSLXEventBus(ILogger<MSLXEventBus> logger)
    {
        _logger = logger;
    }

    #region 事件定义
    public event EventHandler<BackupStartingEventArgs>? OnBackupStarting;
    public event EventHandler<BackupCompletedEventArgs>? OnBackupCompleted;
    public event EventHandler<BackupFailedEventArgs>? OnBackupFailed;
    public event EventHandler<BackupDeletedEventArgs>? OnBackupDeleted;

    public event EventHandler<ServerStartingEventArgs>? OnServerStarting;
    public event EventHandler<ServerStartedEventArgs>? OnServerStarted;
    public event EventHandler<ServerStoppingEventArgs>? OnServerStopping;
    public event EventHandler<ServerStoppedEventArgs>? OnServerStopped;
    public event EventHandler<ServerCrashedEventArgs>? OnServerCrashed;

    public event EventHandler<ServerLogEventArgs>? OnServerLogReceived;
    public event EventHandler<ServerCommandExecutingEventArgs>? OnServerCommandExecuting;

    public event EventHandler<TaskExecutingEventArgs>? OnTaskExecuting;
    public event EventHandler<TaskExecutedEventArgs>? OnTaskExecuted;

    public event EventHandler<FrpStartingEventArgs>? OnFrpStarting;
    public event EventHandler<FrpStartedEventArgs>? OnFrpStarted;
    public event EventHandler<FrpStoppingEventArgs>? OnFrpStopping;
    public event EventHandler<FrpStoppedEventArgs>? OnFrpStopped;
    public event EventHandler<FrpLogEventArgs>? OnFrpLogReceived;
    #endregion

    #region 安全发布实现
    private void SafeInvoke<TEventArgs>(EventHandler<TEventArgs>? eventHandler, TEventArgs args, string eventName) where TEventArgs : EventArgs
    {
        if (eventHandler == null) return;

        var invocationList = eventHandler.GetInvocationList();
        foreach (var handler in invocationList)
        {
            try
            {
                ((EventHandler<TEventArgs>)handler)(this, args);
            }
            catch (Exception ex)
            {
                var targetPlugin = handler.Method.DeclaringType?.Assembly.GetName().Name ?? "Unknown";
                _logger.LogError(ex, "[MSLX EventBus] 插件 [{Plugin}] 执行事件 [{EventName}] 时发生异常: {Message}", targetPlugin, eventName, ex.Message);
            }
        }
    }

    public void PublishBackupStarting(BackupStartingEventArgs e) => SafeInvoke(OnBackupStarting, e, nameof(OnBackupStarting));
    public void PublishBackupCompleted(BackupCompletedEventArgs e) => SafeInvoke(OnBackupCompleted, e, nameof(OnBackupCompleted));
    public void PublishBackupFailed(BackupFailedEventArgs e) => SafeInvoke(OnBackupFailed, e, nameof(OnBackupFailed));
    public void PublishBackupDeleted(BackupDeletedEventArgs e) => SafeInvoke(OnBackupDeleted, e, nameof(OnBackupDeleted));

    public void PublishServerStarting(ServerStartingEventArgs e) => SafeInvoke(OnServerStarting, e, nameof(OnServerStarting));
    public void PublishServerStarted(ServerStartedEventArgs e) => SafeInvoke(OnServerStarted, e, nameof(OnServerStarted));
    public void PublishServerStopping(ServerStoppingEventArgs e) => SafeInvoke(OnServerStopping, e, nameof(OnServerStopping));
    public void PublishServerStopped(ServerStoppedEventArgs e) => SafeInvoke(OnServerStopped, e, nameof(OnServerStopped));
    public void PublishServerCrashed(ServerCrashedEventArgs e) => SafeInvoke(OnServerCrashed, e, nameof(OnServerCrashed));

    public void PublishServerLogReceived(ServerLogEventArgs e) => SafeInvoke(OnServerLogReceived, e, nameof(OnServerLogReceived));
    public void PublishServerCommandExecuting(ServerCommandExecutingEventArgs e) => SafeInvoke(OnServerCommandExecuting, e, nameof(OnServerCommandExecuting));

    public void PublishTaskExecuting(TaskExecutingEventArgs e) => SafeInvoke(OnTaskExecuting, e, nameof(OnTaskExecuting));
    public void PublishTaskExecuted(TaskExecutedEventArgs e) => SafeInvoke(OnTaskExecuted, e, nameof(OnTaskExecuted));

    public void PublishFrpStarting(FrpStartingEventArgs e) => SafeInvoke(OnFrpStarting, e, nameof(OnFrpStarting));
    public void PublishFrpStarted(FrpStartedEventArgs e) => SafeInvoke(OnFrpStarted, e, nameof(OnFrpStarted));
    public void PublishFrpStopping(FrpStoppingEventArgs e) => SafeInvoke(OnFrpStopping, e, nameof(OnFrpStopping));
    public void PublishFrpStopped(FrpStoppedEventArgs e) => SafeInvoke(OnFrpStopped, e, nameof(OnFrpStopped));
    public void PublishFrpLogReceived(FrpLogEventArgs e) => SafeInvoke(OnFrpLogReceived, e, nameof(OnFrpLogReceived));
    #endregion

    #region 插件自动反注册
    public void UnregisterPlugin(Assembly pluginAssembly)
    {
        if (pluginAssembly == null) return;

        lock (_lock)
        {
            int removedCount = 0;

            OnBackupStarting = RemoveHandlersFromAssembly(OnBackupStarting, pluginAssembly, ref removedCount);
            OnBackupCompleted = RemoveHandlersFromAssembly(OnBackupCompleted, pluginAssembly, ref removedCount);
            OnBackupFailed = RemoveHandlersFromAssembly(OnBackupFailed, pluginAssembly, ref removedCount);
            OnBackupDeleted = RemoveHandlersFromAssembly(OnBackupDeleted, pluginAssembly, ref removedCount);

            OnServerStarting = RemoveHandlersFromAssembly(OnServerStarting, pluginAssembly, ref removedCount);
            OnServerStarted = RemoveHandlersFromAssembly(OnServerStarted, pluginAssembly, ref removedCount);
            OnServerStopping = RemoveHandlersFromAssembly(OnServerStopping, pluginAssembly, ref removedCount);
            OnServerStopped = RemoveHandlersFromAssembly(OnServerStopped, pluginAssembly, ref removedCount);
            OnServerCrashed = RemoveHandlersFromAssembly(OnServerCrashed, pluginAssembly, ref removedCount);

            OnServerLogReceived = RemoveHandlersFromAssembly(OnServerLogReceived, pluginAssembly, ref removedCount);
            OnServerCommandExecuting = RemoveHandlersFromAssembly(OnServerCommandExecuting, pluginAssembly, ref removedCount);

            OnTaskExecuting = RemoveHandlersFromAssembly(OnTaskExecuting, pluginAssembly, ref removedCount);
            OnTaskExecuted = RemoveHandlersFromAssembly(OnTaskExecuted, pluginAssembly, ref removedCount);

            OnFrpStarting = RemoveHandlersFromAssembly(OnFrpStarting, pluginAssembly, ref removedCount);
            OnFrpStarted = RemoveHandlersFromAssembly(OnFrpStarted, pluginAssembly, ref removedCount);
            OnFrpStopping = RemoveHandlersFromAssembly(OnFrpStopping, pluginAssembly, ref removedCount);
            OnFrpStopped = RemoveHandlersFromAssembly(OnFrpStopped, pluginAssembly, ref removedCount);
            OnFrpLogReceived = RemoveHandlersFromAssembly(OnFrpLogReceived, pluginAssembly, ref removedCount);

            if (removedCount > 0)
            {
                _logger.LogInformation("[MSLX EventBus] 已自动注销插件 [{Plugin}] 的 {Count} 个事件监听器。", pluginAssembly.GetName().Name, removedCount);
            }
        }
    }

    private EventHandler<TEventArgs>? RemoveHandlersFromAssembly<TEventArgs>(EventHandler<TEventArgs>? source, Assembly assembly, ref int count) where TEventArgs : EventArgs
    {
        if (source == null) return null;

        var invocationList = source.GetInvocationList();
        foreach (var handler in invocationList)
        {
            if (handler.Method.DeclaringType?.Assembly == assembly || handler.Target?.GetType().Assembly == assembly)
            {
                source = (EventHandler<TEventArgs>?)Delegate.Remove(source, handler);
                count++;
            }
        }

        return source;
    }
    #endregion
}
