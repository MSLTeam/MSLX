using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.Models;
using MSLX.SDK.Models.Node;

namespace MSLX.Daemon.Controllers.NodesControllers
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
        [HttpGet("protocol")]
        public IActionResult Protocol() => Ok(new ApiResponse<object> { Code = 200, Message = "成功",
            Data = new { protocolVersion = JwtUtils.NodeProtocolVersion } });

        [AllowAnonymous]
        [HttpPost("remove-link")]
        public IActionResult RemoveLink([FromBody] UpdateLinkRequest request)
        {
            IConfigBase.MasterNodes.RemoveRelationship(request.NodeId, request.CommsKey);
            return Ok(new ApiResponse<object> { Code = 200, Message = "已清理链接" });
        }

        [AllowAnonymous]
        [HttpPost("accept-link")]
        public IActionResult AcceptLink([FromBody] AcceptLinkRequest request)
        {
            if (request.ProtocolVersion != JwtUtils.NodeProtocolVersion)
                return BadRequest(new ApiResponse<object> { Code = 400, Message = "节点协议不兼容，请一起升级" });
            bool isSlaveMode = bool.Parse(IConfigBase.Config.ReadConfigKey("IsSlaveMode")?.ToString() ?? "false");
            if (!isSlaveMode) return BadRequest(new ApiResponse<object> { Code = 400, Message = "当前并非子节点模式，拒绝链接请求" });

            string currentLinkKey = IConfigBase.Config.ReadConfigKey("SlaveLinkKey")?.ToString() ?? "";
            if (string.IsNullOrEmpty(currentLinkKey) || currentLinkKey != request.LinkKey)
            {
                _logger.LogWarning("子节点拒绝链接: LinkKey 验证不匹配。客户端主节点地址: {MasterUrl}", request.MasterUrl);
                return Unauthorized(new ApiResponse<object> { Code = 401, Message = "子节点 LinkKey 错误" });
            }

            if (!Guid.TryParseExact(request.PairingId, "N", out _))
                return BadRequest(new ApiResponse<object> { Code = 400, Message = "配对标识无效" });
            var master = IConfigBase.MasterNodes.AcceptPairing(request);
            return Ok(new ApiResponse<object> { Code = 200, Message = "链接成功", Data = new { nodeId = master.NodeId, commsKey = master.CommsKey } });
        }

        [AllowAnonymous]
        [HttpPost("update-link")]
        public IActionResult UpdateLink([FromBody] UpdateLinkRequest request)
        {
            if (!IConfigBase.MasterNodes.UpdateRelationship(request))
                return Unauthorized(new ApiResponse<object> { Code = 401, Message = "节点关系已变化或通讯密钥无效" });

            _logger.LogInformation("更新链接成功。同步的 MasterUrl: {MasterUrl}, NodeId: {NodeId}", request.MasterUrl, request.NodeId);

            return Ok(new ApiResponse<object> { Code = 200, Message = "同步成功" });
        }
    }
}
