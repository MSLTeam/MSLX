using MSLX.SDK.Events;
using MSLX.SDK.Interfaces;
using System.Reflection;

namespace MSLX.Tests.Fakes;

/// <summary>
/// IMSLXEvents 的假实现：事件订阅全部丢弃，Publish 方法默认空操作，
/// 仅记录 PublishServerLogReceived 供断言。
/// </summary>
public class FakeMSLXEvents : IMSLXEvents
{
    public List<ServerLogEventArgs> PublishedLogs { get; } = new();
    public List<BackupStartingEventArgs> StartedBackups { get; } = new();
    public List<BackupCompletedEventArgs> CompletedBackups { get; } = new();
    public List<BackupFailedEventArgs> FailedBackups { get; } = new();

    /// <summary>置为 true 后，下一次 PublishBackupStarting 会设置取消标记（模拟插件拦截）</summary>
    public bool CancelNextBackupStarting { get; set; }

#pragma warning disable CS0067 // 事件从未使用（fake 有意不触发）
    public event EventHandler<BackupStartingEventArgs>? OnBackupStarting { add { } remove { } }
    public event EventHandler<BackupCompletedEventArgs>? OnBackupCompleted { add { } remove { } }
    public event EventHandler<BackupFailedEventArgs>? OnBackupFailed { add { } remove { } }
    public event EventHandler<BackupDeletedEventArgs>? OnBackupDeleted { add { } remove { } }
    public event EventHandler<ServerStartingEventArgs>? OnServerStarting { add { } remove { } }
    public event EventHandler<ServerStartedEventArgs>? OnServerStarted { add { } remove { } }
    public event EventHandler<ServerStoppingEventArgs>? OnServerStopping { add { } remove { } }
    public event EventHandler<ServerStoppedEventArgs>? OnServerStopped { add { } remove { } }
    public event EventHandler<ServerCrashedEventArgs>? OnServerCrashed { add { } remove { } }
    public event EventHandler<ServerLogEventArgs>? OnServerLogReceived { add { } remove { } }
    public event EventHandler<ServerCommandExecutingEventArgs>? OnServerCommandExecuting { add { } remove { } }
    public event EventHandler<TaskExecutingEventArgs>? OnTaskExecuting { add { } remove { } }
    public event EventHandler<TaskExecutedEventArgs>? OnTaskExecuted { add { } remove { } }
    public event EventHandler<FrpStartingEventArgs>? OnFrpStarting { add { } remove { } }
    public event EventHandler<FrpStartedEventArgs>? OnFrpStarted { add { } remove { } }
    public event EventHandler<FrpStoppingEventArgs>? OnFrpStopping { add { } remove { } }
    public event EventHandler<FrpStoppedEventArgs>? OnFrpStopped { add { } remove { } }
    public event EventHandler<FrpLogEventArgs>? OnFrpLogReceived { add { } remove { } }
#pragma warning restore CS0067

    public void PublishBackupStarting(BackupStartingEventArgs e)
    {
        StartedBackups.Add(e);
        if (CancelNextBackupStarting)
        {
            e.Cancel = true;
            e.CancelReason = "test-cancel";
            CancelNextBackupStarting = false;
        }
    }
    public void PublishBackupCompleted(BackupCompletedEventArgs e) => CompletedBackups.Add(e);
    public void PublishBackupFailed(BackupFailedEventArgs e) => FailedBackups.Add(e);
    public void PublishBackupDeleted(BackupDeletedEventArgs e) { }
    public void PublishServerStarting(ServerStartingEventArgs e) { }
    public void PublishServerStarted(ServerStartedEventArgs e) { }
    public void PublishServerStopping(ServerStoppingEventArgs e) { }
    public void PublishServerStopped(ServerStoppedEventArgs e) { }
    public void PublishServerCrashed(ServerCrashedEventArgs e) { }
    public void PublishServerLogReceived(ServerLogEventArgs e) => PublishedLogs.Add(e);
    public void PublishServerCommandExecuting(ServerCommandExecutingEventArgs e) { }
    public void PublishTaskExecuting(TaskExecutingEventArgs e) { }
    public void PublishTaskExecuted(TaskExecutedEventArgs e) { }
    public void PublishFrpStarting(FrpStartingEventArgs e) { }
    public void PublishFrpStarted(FrpStartedEventArgs e) { }
    public void PublishFrpStopping(FrpStoppingEventArgs e) { }
    public void PublishFrpStopped(FrpStoppedEventArgs e) { }
    public void PublishFrpLogReceived(FrpLogEventArgs e) { }
    public void UnregisterPlugin(Assembly pluginAssembly) { }
}
