using System.Threading.Channels;
using MSLX.Daemon.Models.Tasks;

namespace MSLX.Daemon.Utils.BackgroundTasks.BackgroundTasks;

/// <summary>
/// 基于 Channel 的内存后台任务队列
/// </summary>
public class BackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly Channel<CreateServerTask> _queue;

    public BackgroundTaskQueue(int capacity)
    {
        // 配置队列容量和当队列满时的行为（等待）
        var options = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait
        };
        _queue = Channel.CreateBounded<CreateServerTask>(options);
    }

    public async ValueTask QueueTaskAsync(CreateServerTask task)
    {
        if (task == null)
        {
            throw new ArgumentNullException(nameof(task));
        }
        await _queue.Writer.WriteAsync(task);
    }

    public async ValueTask<CreateServerTask> DequeueTaskAsync(CancellationToken cancellationToken)
    {
        var task = await _queue.Reader.ReadAsync(cancellationToken);
        return task;
    }
}