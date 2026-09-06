using MSLX.SDK.Models.Instance;

namespace MSLX.SDK.Models.Tasks;
    /// <summary>
    /// 封装一个创建服务器任务所需的所有信息
    /// </summary>
    public class CreateServerTask
    {
        /// <summary>
        /// 唯一的任务ID，也将用作 ServerId 和 SignalR 组名
        /// </summary>
        public string ServerId { get; set; }

        /// <summary>
        /// 关联的后台任务ID
        /// </summary>
        public string BackgroundTaskId { get; set; } = string.Empty;

        /// <summary>
        /// 提交任务的用户ID
        /// </summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>
        /// 客户端发来的原始请求数据
        /// </summary>
        public CreateServerRequest Request { get; set; }
    }