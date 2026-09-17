using MSLX.SDK.Models;

namespace MSLX.SDK.Events;

/// <summary>
/// 备份即将开始事件参数
/// </summary>
public class BackupStartingEventArgs : EventArgs
{
    public uint InstanceId { get; set; }
    public McServerInfo.ServerInfo? ServerInfo { get; set; }
    public string BackupDirectory { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;

    /// <summary>
    /// 是否取消本次备份（若为 true，开服器将跳过备份流程）
    /// </summary>
    public bool Cancel { get; set; } = false;

    /// <summary>
    /// 取消原因
    /// </summary>
    public string? CancelReason { get; set; }
}

/// <summary>
/// 备份成功完成事件参数（可用于触发 GFS 分层归档、二次压缩与云端转存）
/// </summary>
public class BackupCompletedEventArgs : EventArgs
{
    public uint InstanceId { get; set; }
    public McServerInfo.ServerInfo? ServerInfo { get; set; }

    /// <summary>
    /// 备份 zip 压缩包完整绝对路径
    /// </summary>
    public string BackupFilePath { get; set; } = string.Empty;

    /// <summary>
    /// 备份文件名（如 mslx-backup_20260312_120000.zip）
    /// </summary>
    public string BackupFileName { get; set; } = string.Empty;

    /// <summary>
    /// 备份文件大小（字节）
    /// </summary>
    public long FileSizeBytes { get; set; }

    /// <summary>
    /// 格式化文件大小字符串（如 23.45 MB）
    /// </summary>
    public string FormattedSize { get; set; } = string.Empty;

    /// <summary>
    /// 备份执行耗时
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// 备份完成时间
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

/// <summary>
/// 备份失败事件参数
/// </summary>
public class BackupFailedEventArgs : EventArgs
{
    public uint InstanceId { get; set; }
    public McServerInfo.ServerInfo? ServerInfo { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public Exception? Exception { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

/// <summary>
/// 备份文件被删除事件参数
/// </summary>
public class BackupDeletedEventArgs : EventArgs
{
    public uint InstanceId { get; set; }
    public string BackupFilePath { get; set; } = string.Empty;
    public string BackupFileName { get; set; } = string.Empty;

    /// <summary>
    /// 是否由于超出最大备份限制而执行的自动滚动删除
    /// </summary>
    public bool IsAutoRoll { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.Now;
}
