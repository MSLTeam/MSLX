using System.Threading.Channels;

namespace MSLX.Daemon.Utils.BackgroundTasks;

public class BackgroundTaskQueue<T> : IBackgroundTaskQueue<T>
{
    private readonly Channel<T> _queue;

    public BackgroundTaskQueue(int capacity = 100)
    {
        // 配置通道
        var options = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait
        };
        _queue = Channel.CreateBounded<T>(options);
    }

    public async ValueTask QueueTaskAsync(T task)
    {
        if (task == null) throw new ArgumentNullException(nameof(task));
        await _queue.Writer.WriteAsync(task);
    }

    public async ValueTask<T> DequeueTaskAsync(CancellationToken cancellationToken)
    {
        return await _queue.Reader.ReadAsync(cancellationToken);
    }
}