using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.Models;
using MSLX.SDK.Models.Node;
using Newtonsoft.Json.Linq;

namespace MSLX.Daemon.Controllers.NodesControllers
{
    [ApiController]
    [Route("api/node")]
    public class SlaveNodeController : ControllerBase
    {
        private readonly ILogger<SlaveNodeController> _logger;
        private static readonly HttpClient MasterProbeClient = new() { Timeout = TimeSpan.FromSeconds(5) };

        public SlaveNodeController(ILogger<SlaveNodeController> logger)
        {
            _logger = logger;
        }

        [AllowAnonymous]
        [HttpGet("protocol")]
        public IActionResult Protocol([FromQuery] string? pairingId = null)
        {
            if (pairingId != null)
            {
                if (!MasterNodeController.IsPairingInProgress(pairingId))
                    return BadRequest(new ApiResponse<object> { Code = 400, Message = "此主节点没有对应的配对请求" });
                return Ok(new ApiResponse<object> { Code = 200, Message = "成功",
                    Data = new { protocolVersion = JwtUtils.NodeProtocolVersion, pairingId, features = new { pluginMultiNode = true, nodeSync = true } } });
            }
            return Ok(new ApiResponse<object> { Code = 200, Message = "成功",
                Data = new { protocolVersion = JwtUtils.NodeProtocolVersion, features = new { pluginMultiNode = true, nodeSync = true } } });
        }

        private static async Task<string?> CheckMasterConnectionAsync(AcceptLinkRequest request, HttpClient client, CancellationToken cancellationToken)
        {
            const string error = "子节点连接主节点通讯失败";
            if (!Uri.TryCreate(request.MasterUrl, UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                return $"{error}：主节点地址必须为有效 HTTP/HTTPS 地址";
            request.MasterUrl = uri.AbsoluteUri.TrimEnd('/');
            try
            {
                using var response = await client.GetAsync($"{request.MasterUrl}/api/node/protocol?pairingId={request.PairingId}", cancellationToken);
                if (!response.IsSuccessStatusCode)
                    return $"{error}（HTTP {(int)response.StatusCode}）：{request.MasterUrl}，请检查该地址是否指向当前主节点，以及端口和反向代理配置";
                var body = JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                if (body["code"]?.ToString() != "200" || body["data"] is not JObject data ||
                    data["protocolVersion"]?.ToString() != JwtUtils.NodeProtocolVersion.ToString() ||
                    data["pairingId"]?.Type != JTokenType.String || data["pairingId"]!.Value<string>() != request.PairingId)
                    return $"{error}：{request.MasterUrl} 未确认本次配对，请填写子节点能够访问的当前主节点地址";
                return null;
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or Newtonsoft.Json.JsonException)
            {
                return $"{error}：{request.MasterUrl}，" + (ex switch
                {
                    OperationCanceledException => "连接超时，请检查网络及端口",
                    Newtonsoft.Json.JsonException => "响应不是有效的 MSLX 接口数据，请检查地址、端口及反向代理配置",
                    _ => "请检查子节点能否访问此地址、端口及 HTTP/HTTPS 配置；localhost 指向子节点自身"
                });
            }
        }

        [AllowAnonymous]
        [HttpPost("remove-link")]
        public IActionResult RemoveLink([FromBody] UpdateLinkRequest request)
        {
            IConfigBase.MasterNodes.RemoveRelationship(request.NodeId, request.CommsKey);
            return Ok(new ApiResponse<object> { Code = 200, Message = "已清理链接" });
        }

        [AllowAnonymous]
        [HttpPost("accept-link")]
        public async Task<IActionResult> AcceptLink([FromBody] AcceptLinkRequest request)
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
            var connectionError = await CheckMasterConnectionAsync(request, MasterProbeClient, HttpContext.RequestAborted);
            if (connectionError != null)
            {
                _logger.LogWarning("{Message}", connectionError);
                return BadRequest(new ApiResponse<object> { Code = 400, Message = connectionError });
            }
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
