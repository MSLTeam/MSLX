using System.Collections.Concurrent;
using MSLX.SDK.Models.Files;
using MSLX.SDK.IServices;

namespace MSLX.Daemon.Services;

public class BackgroundTaskManager : IBackgroundTaskManager
{
    private readonly ConcurrentDictionary<string, BackgroundTaskItem> _tasks = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _ctsMap = new();
    private readonly ILogger<BackgroundTaskManager> _logger;
    private readonly Timer _cleanupTimer;

    public BackgroundTaskManager(ILogger<BackgroundTaskManager> logger)
    {
        _logger = logger;
        _cleanupTimer = new Timer(CleanupExpiredTasks, null, TimeSpan.FromMinutes(10), TimeSpan.FromHours(1));
    }

    public (BackgroundTaskItem, CancellationToken) CreateTask(string userId, uint instanceId, TaskType type, string title, string targetName)
    {
        var task = new BackgroundTaskItem
        {
            UserId = userId,
            InstanceId = instanceId,
            Type = type,
            Title = title,
            TargetName = targetName,
            State = TaskState.Pending,
            Message = "任务已排队，准备开始...",
            CreatedAt = DateTime.Now
        };

        var cts = new CancellationTokenSource();
        _tasks[task.Id] = task;
        _ctsMap[task.Id] = cts;

        return (task, cts.Token);
    }

    public void UpdateProgress(string taskId, int progress, string message, TaskState state = TaskState.Running)
    {
        if (_tasks.TryGetValue(taskId, out var task))
        {
            task.Progress = progress;
            task.Message = message;
            if (task.State != state)
            {
                task.State = state;
            }
        }
    }

    public void SetSuccess(string taskId, string message = "已完成")
    {
        if (_tasks.TryGetValue(taskId, out var task))
        {
            task.State = TaskState.Success;
            task.Progress = 100;
            task.Message = message;
            task.FinishedAt = DateTime.Now;
            _ctsMap.TryRemove(taskId, out _);
        }
    }

    public void SetFailed(string taskId, string error)
    {
        if (_tasks.TryGetValue(taskId, out var task))
        {
            task.State = TaskState.Failed;
            task.Message = error;
            task.FinishedAt = DateTime.Now;
            _ctsMap.TryRemove(taskId, out _);
        }
    }

    public bool CancelTask(string taskId, string userId, bool isAdmin)
    {
        if (_tasks.TryGetValue(taskId, out var task))
        {
            if (!isAdmin && task.UserId != userId)
                return false;

            if (task.State == TaskState.Pending || task.State == TaskState.Running)
            {
                task.State = TaskState.Canceled;
                task.Message = "任务已取消";
                task.FinishedAt = DateTime.Now;

                if (_ctsMap.TryRemove(taskId, out var cts))
                {
                    cts.Cancel();
                    cts.Dispose();
                }
                return true;
            }
        }
        return false;
    }

    public BackgroundTaskItem? GetTask(string taskId)
    {
        _tasks.TryGetValue(taskId, out var task);
        return task;
    }

    public IEnumerable<BackgroundTaskItem> GetUserTasks(string userId, uint? instanceId = null)
    {
        var query = _tasks.Values.Where(t => t.UserId == userId);
        if (instanceId.HasValue)
        {
            query = query.Where(t => t.InstanceId == instanceId.Value);
        }
        return query.OrderByDescending(t => t.CreatedAt);
    }

    public bool DeleteTask(string taskId, string userId, bool isAdmin)
    {
        if (_tasks.TryGetValue(taskId, out var task))
        {
            if (!isAdmin && task.UserId != userId)
                return false;

            if (task.State == TaskState.Success || task.State == TaskState.Failed || task.State == TaskState.Canceled)
            {
                _tasks.TryRemove(taskId, out _);
                return true;
            }
        }
        return false;
    }

    public void ClearFinished(string userId)
    {
        var finishedTasks = _tasks.Values.Where(t => t.UserId == userId && (t.State == TaskState.Success || t.State == TaskState.Failed || t.State == TaskState.Canceled)).ToList();
        foreach (var task in finishedTasks)
        {
            _tasks.TryRemove(task.Id, out _);
        }
    }

    private void CleanupExpiredTasks(object? state)
    {
        var threshold = DateTime.Now.AddHours(-2);
        var expiredTasks = _tasks.Values
            .Where(t => t.FinishedAt.HasValue && t.FinishedAt.Value < threshold)
            .ToList();

        foreach (var task in expiredTasks)
        {
            _tasks.TryRemove(task.Id, out _);
            _logger.LogInformation($"[Task Manager] 已自动清理过期任务: {task.Id}");
        }
    }
}
