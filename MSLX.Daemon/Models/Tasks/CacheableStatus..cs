namespace MSLX.Daemon.Models.Tasks
{
    /// <summary>
    /// 用于在 IMemoryCache 中存储的最终状态对象
    /// </summary>
    public class CacheableStatus
    {
        public string Message { get; set; }
        public double? Progress { get; set; }
    }
}