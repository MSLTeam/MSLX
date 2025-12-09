namespace MSLX.Daemon.Utils.BackgroundTasks;

// 注意这里的 <T>，表示这个队列是“通用”的
public interface IBackgroundTaskQueue<T>
{
    // 添加一个任务
    ValueTask QueueTaskAsync(T task);
    // 把任务从队列取出来
    ValueTask<T> DequeueTaskAsync(CancellationToken cancellationToken);
}