using MSLX.Daemon.Models.Tasks;

namespace MSLX.Daemon.Utils.BackgroundTasks;

public interface IBackgroundTaskQueue
{
    /// <summary>
    /// 将任务加入队列
    /// </summary>
    ValueTask QueueTaskAsync(CreateServerTask task);

    /// <summary>
    /// 从队列中取出任务
    /// </summary>
    ValueTask<CreateServerTask> DequeueTaskAsync(CancellationToken cancellationToken);
}