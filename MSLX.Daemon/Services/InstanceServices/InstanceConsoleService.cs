namespace MSLX.Daemon.Services.InstanceServices;

/// <summary>
/// 实例控制台输出的缓冲层：日志环形缓冲与 PTY 历史块缓冲。
/// 注册为 DI 单例。
/// </summary>
public class InstanceConsoleService
{
    /// <summary>每个实例最多保留的日志行数</summary>
    public const int MaxLogLines = 1000;

    /// <summary>每个实例最多保留的 PTY 历史数据块数</summary>
    public const int MaxPtyHistoryChunks = 100;

    /// <summary>
    /// 追加一行日志到实例的日志缓冲，超出 <see cref="MaxLogLines"/> 时丢弃最旧的行
    /// </summary>
    public void AppendLog(ServerContext context, string line)
    {
        context.Logs.Enqueue(line);
        while (context.Logs.Count > MaxLogLines)
            context.Logs.TryDequeue(out _);
    }

    /// <summary>
    /// 追加一块 PTY 原始输出到历史缓冲，超出 <see cref="MaxPtyHistoryChunks"/> 时丢弃最旧的块
    /// </summary>
    public void AppendPtyHistory(ServerContext context, string chunk)
    {
        context.PtyHistory.Enqueue(chunk);
        while (context.PtyHistory.Count > MaxPtyHistoryChunks)
            context.PtyHistory.TryDequeue(out _);
    }

    /// <summary>获取实例当前的日志缓冲快照，实例不存在时返回空列表</summary>
    public List<string> GetLogs(ServerContext? context)
        => context?.Logs.ToList() ?? new List<string>();

    /// <summary>获取实例当前的 PTY 历史缓冲快照，实例不存在时返回空列表</summary>
    public List<string> GetPtyHistory(ServerContext? context)
        => context?.PtyHistory.ToList() ?? new List<string>();
}
