using System.Reflection;
using MSLX.SDK.Events;

namespace MSLX.SDK.Interfaces;

/// <summary>
/// MSLX 全局事件总线，供插件订阅服务端与守护进程的核心生命周期事件
/// </summary>
public interface IMSLXEvents
{
    #region 备份相关事件
    /// <summary>
    /// 备份即将开始事件
    /// </summary>
    event EventHandler<BackupStartingEventArgs>? OnBackupStarting;

    /// <summary>
    /// 备份完成事件（可用于做 GFS 分层归档、压缩、云端转存）
    /// </summary>
    event EventHandler<BackupCompletedEventArgs>? OnBackupCompleted;

    /// <summary>
    /// 备份失败事件
    /// </summary>
    event EventHandler<BackupFailedEventArgs>? OnBackupFailed;

    /// <summary>
    /// 备份被删除事件
    /// </summary>
    event EventHandler<BackupDeletedEventArgs>? OnBackupDeleted;
    #endregion

    #region 服务器生命周期事件
    /// <summary>
    /// 服务器准备启动事件
    /// </summary>
    event EventHandler<ServerStartingEventArgs>? OnServerStarting;

    /// <summary>
    /// 服务器启动就绪/进程已启动事件
    /// </summary>
    event EventHandler<ServerStartedEventArgs>? OnServerStarted;

    /// <summary>
    /// 服务器准备停止事件
    /// </summary>
    event EventHandler<ServerStoppingEventArgs>? OnServerStopping;

    /// <summary>
    /// 服务器完全退出事件
    /// </summary>
    event EventHandler<ServerStoppedEventArgs>? OnServerStopped;

    /// <summary>
    /// 服务器异常崩溃退出事件
    /// </summary>
    event EventHandler<ServerCrashedEventArgs>? OnServerCrashed;
    #endregion

    #region 控制台与命令事件
    /// <summary>
    /// 接收到服务器控制台输出行事件（适用于群服互通、关键词监控）
    /// </summary>
    event EventHandler<ServerLogEventArgs>? OnServerLogReceived;

    /// <summary>
    /// 执行命令前事件（适用于敏感指令审计或拦截）
    /// </summary>
    event EventHandler<ServerCommandExecutingEventArgs>? OnServerCommandExecuting;
    #endregion

    #region 计划任务事件
    /// <summary>
    /// 计划任务开始执行事件
    /// </summary>
    event EventHandler<TaskExecutingEventArgs>? OnTaskExecuting;

    /// <summary>
    /// 计划任务执行完毕事件
    /// </summary>
    event EventHandler<TaskExecutedEventArgs>? OnTaskExecuted;
    #endregion

    #region FRP 隧道事件
    /// <summary>
    /// FRP 隧道准备启动事件
    /// </summary>
    event EventHandler<FrpStartingEventArgs>? OnFrpStarting;

    /// <summary>
    /// FRP 隧道启动就绪/进程已启动事件
    /// </summary>
    event EventHandler<FrpStartedEventArgs>? OnFrpStarted;

    /// <summary>
    /// FRP 隧道正在停止事件
    /// </summary>
    event EventHandler<FrpStoppingEventArgs>? OnFrpStopping;

    /// <summary>
    /// FRP 隧道已完全停止退出事件
    /// </summary>
    event EventHandler<FrpStoppedEventArgs>? OnFrpStopped;

    /// <summary>
    /// FRP 隧道控制台日志接收事件
    /// </summary>
    event EventHandler<FrpLogEventArgs>? OnFrpLogReceived;
    #endregion

    #region 触发/广播方法 (供宿主各业务层调用)
    void PublishBackupStarting(BackupStartingEventArgs e);
    void PublishBackupCompleted(BackupCompletedEventArgs e);
    void PublishBackupFailed(BackupFailedEventArgs e);
    void PublishBackupDeleted(BackupDeletedEventArgs e);

    void PublishServerStarting(ServerStartingEventArgs e);
    void PublishServerStarted(ServerStartedEventArgs e);
    void PublishServerStopping(ServerStoppingEventArgs e);
    void PublishServerStopped(ServerStoppedEventArgs e);
    void PublishServerCrashed(ServerCrashedEventArgs e);

    void PublishServerLogReceived(ServerLogEventArgs e);
    void PublishServerCommandExecuting(ServerCommandExecutingEventArgs e);

    void PublishTaskExecuting(TaskExecutingEventArgs e);
    void PublishTaskExecuted(TaskExecutedEventArgs e);

    void PublishFrpStarting(FrpStartingEventArgs e);
    void PublishFrpStarted(FrpStartedEventArgs e);
    void PublishFrpStopping(FrpStoppingEventArgs e);
    void PublishFrpStopped(FrpStoppedEventArgs e);
    void PublishFrpLogReceived(FrpLogEventArgs e);

    /// <summary>
    /// 卸载插件时，自动注销该插件 Assembly 下注册的所有事件处理程序，杜绝内存泄漏与悬挂引用
    /// </summary>
    /// <param name="pluginAssembly">插件程序集</param>
    void UnregisterPlugin(Assembly pluginAssembly);
    #endregion
}
