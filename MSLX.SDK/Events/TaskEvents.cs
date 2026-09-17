namespace MSLX.SDK.Events;

/// <summary>
/// 计划任务开始执行事件参数
/// </summary>
public class TaskExecutingEventArgs : EventArgs
{
    public string TaskId { get; set; } = string.Empty;
    public string TaskName { get; set; } = string.Empty;
    public string TaskType { get; set; } = string.Empty;
    public uint InstanceId { get; set; }
    public bool Cancel { get; set; } = false;
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

/// <summary>
/// 计划任务执行完毕事件参数
/// </summary>
public class TaskExecutedEventArgs : EventArgs
{
    public string TaskId { get; set; } = string.Empty;
    public string TaskName { get; set; } = string.Empty;
    public string TaskType { get; set; } = string.Empty;
    public uint InstanceId { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
