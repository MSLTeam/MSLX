using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory; // 1. 【新增】 引入缓存
using MSLX.Daemon.Models.Tasks; // 2. 【新增】 引入我们定义的状态模型
using System.Threading.Tasks;

namespace MSLX.Daemon.Hubs
{
    /// <summary>
    /// 用于广播服务器创建进度的 SignalR Hub
    /// </summary>
    public class CreationProgressHub : Hub
    {
        // IMemoryCache 注入
        private readonly IMemoryCache _memoryCache;

        public CreationProgressHub(IMemoryCache memoryCache)
        {
            _memoryCache = memoryCache;
        }

        /// <summary>
        /// 客户端调用此方法来"订阅"特定服务器ID的进度更新
        /// </summary>
        /// <param name="serverId">服务器ID (也是任务ID)</param>
        public async Task TrackServer(string serverId)
        {
            // 在缓存查找这个任务是否已经完成了
            if (_memoryCache.TryGetValue(serverId, out CacheableStatus cachedStatus))
            {
                // 任务已经完成了 那么直接发送完成信息
                await Clients.Caller.SendAsync("StatusUpdate", serverId, cachedStatus.Message, cachedStatus.Progress);
            }
            
            // 客户端还是丢进group？
            await Groups.AddToGroupAsync(Context.ConnectionId, serverId);
        }

        /// <summary>
        /// 客户端调用此方法来"取消订阅"
        /// </summary>
        public async Task UnTrackServer(string serverId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, serverId);
        }
    }
}