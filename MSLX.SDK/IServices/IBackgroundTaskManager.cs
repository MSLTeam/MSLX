using MSLX.SDK.Models.Files;

namespace MSLX.SDK.IServices;

/// <summary>
/// 全局后台任务管理器
/// </summary>
public interface IBackgroundTaskManager
{
    /// <summary>
    /// 创建一个新的后台任务
    /// </summary>
    (BackgroundTaskItem task, CancellationToken token) CreateTask(string userId, uint instanceId, TaskType type, string title, string targetName);

    /// <summary>
    /// 更新后台任务进度
    /// </summary>
    void UpdateProgress(string taskId, int progress, string message, TaskState state = TaskState.Running);

    /// <summary>
    /// 将任务标记为成功
    /// </summary>
    void SetSuccess(string taskId, string message = "已完成");

    /// <summary>
    /// 将任务标记为失败
    /// </summary>
    void SetFailed(string taskId, string error);

    /// <summary>
    /// 获取指定的任务
    /// </summary>
    BackgroundTaskItem? GetTask(string taskId);

    /// <summary>
    /// 取消任务
    /// </summary>
    bool CancelTask(string taskId, string userId, bool isAdmin);

    /// <summary>
    /// 删除任务记录
    /// </summary>
    bool DeleteTask(string taskId, string userId, bool isAdmin);
}
