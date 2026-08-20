using System.ComponentModel.DataAnnotations;

namespace MSLX.SDK.Models.Files;

public enum TaskType
{
    Compress,
    Decompress,
    Download,
    Export,
    Plugin,
    CreateServer,
    UpdateServer
}

public enum TaskState
{
    Pending,
    Running,
    Success,
    Failed,
    Canceled
}

public class BackgroundTaskItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string UserId { get; set; } = string.Empty;
    public uint InstanceId { get; set; }
    public TaskType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string TargetName { get; set; } = string.Empty;
    public TaskState State { get; set; } = TaskState.Pending;
    public int Progress { get; set; } = 0;
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? FinishedAt { get; set; }
}
