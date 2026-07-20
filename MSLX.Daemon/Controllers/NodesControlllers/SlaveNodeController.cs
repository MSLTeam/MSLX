using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.Models;
using MSLX.SDK.Models.Node;

namespace MSLX.Daemon.Controllers.NodesControlllers
{
    [ApiController]
    [Route("api/node")]
    public class SlaveNodeController : ControllerBase
    {
        private readonly ILogger<SlaveNodeController> _logger;

        public SlaveNodeController(ILogger<SlaveNodeController> logger)
        {
            _logger = logger;
        }

        [AllowAnonymous]
        [HttpPost("accept-link")]
        public IActionResult AcceptLink([FromBody] AcceptLinkRequest request)
        {
            bool isSlaveMode = bool.Parse(IConfigBase.Config.ReadConfigKey("IsSlaveMode")?.ToString() ?? "false");
            if (!isSlaveMode) return BadRequest(new ApiResponse<object> { Code = 400, Message = "当前并非子节点模式，拒绝链接请求" });

            string currentLinkKey = IConfigBase.Config.ReadConfigKey("SlaveLinkKey")?.ToString() ?? "";
            if (string.IsNullOrEmpty(currentLinkKey) || currentLinkKey != request.LinkKey)
            {
                _logger.LogWarning("子节点拒绝链接: LinkKey 验证不匹配。客户端主节点地址: {MasterUrl}", request.MasterUrl);
                return Unauthorized(new ApiResponse<object> { Code = 401, Message = "子节点 LinkKey 错误" });
            }

            // 生成唯一的 nodeId 和 commsKey
            string nodeId = Guid.NewGuid().ToString("N");
            string commsKey = StringServices.GenerateRandomString(32);

            var masterNode = new MasterNodeInfo
            {
                MasterId = request.MasterId,
                NodeId = nodeId,
                MasterUrl = request.MasterUrl,
                CommsKey = commsKey,
                LinkedAt = DateTime.Now
            };

            IConfigBase.MasterNodes.AddOrUpdateMaster(masterNode);

            _logger.LogInformation("已接受来自主节点的链接。分配 NodeId: {NodeId}, MasterUrl: {MasterUrl}", nodeId, request.MasterUrl);

            return Ok(new ApiResponse<object> { Code = 200, Message = "链接成功", Data = new { nodeId, commsKey } });
        }

        [AllowAnonymous]
        [HttpPost("update-link")]
        public IActionResult UpdateLink([FromBody] UpdateLinkRequest request)
        {
            var masterNode = IConfigBase.MasterNodes.GetAllMasters().FirstOrDefault(m => m.CommsKey == request.CommsKey);
            if (masterNode == null)
            {
                _logger.LogWarning("拒绝更新链接请求: 无效的通讯密钥(CommsKey)");
                return Unauthorized(new ApiResponse<object> { Code = 401, Message = "无效的通讯密钥" });
            }

            masterNode.MasterUrl = request.MasterUrl;
            // 修复老版本缺失 NodeId 的问题
            if (!string.IsNullOrEmpty(request.NodeId))
            {
                masterNode.NodeId = request.NodeId;
            }
            IConfigBase.MasterNodes.AddOrUpdateMaster(masterNode);

            _logger.LogInformation("更新链接成功。同步的 MasterUrl: {MasterUrl}, NodeId: {NodeId}", request.MasterUrl, masterNode.NodeId);

            return Ok(new ApiResponse<object> { Code = 200, Message = "同步成功" });
        }
    }
}
