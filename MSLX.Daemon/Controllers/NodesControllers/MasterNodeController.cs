using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.Models;
using MSLX.SDK.Models.Node;

namespace MSLX.Daemon.Controllers.NodesControllers
{
    [ApiController]
    [Route("api/node")]
    public class MasterNodeController : ControllerBase
    {
        private readonly ILogger<MasterNodeController> _logger;
        private static readonly SemaphoreSlim LinkLock = new(1, 1);
        private static string? _activePairingId;

        internal static bool IsPairingInProgress(string pairingId) =>
            !string.IsNullOrEmpty(pairingId) && pairingId == Volatile.Read(ref _activePairingId);

        private static object PublicNode(SlaveNodeConfig n) => new
        {
            n.NodeId, n.NodeName, n.NodeUrl, n.NodeLogo, n.MasterUrl, n.NodeTags, n.LinkedAt, n.PendingRemoval
        };

        [Authorize]
        [HttpPost("session")]
        public IActionResult CreateSession([FromBody] NodeSessionRequest request)
        {
            if (User.HasClaim(c => c.Type == "Scope") || User.Identity?.AuthenticationType == "NodeAuth" ||
                User.FindFirst("exp") == null || User.FindFirst("jti") == null)
                return Forbid();
            if (request.Scope is not (null or "" or "media" or "download")) return BadRequest();
            var node = IConfigBase.NodeList.GetNodeById(request.NodeId);
            if (node == null || node.PendingRemoval) return NotFound();
            var user = IConfigBase.UserList.GetUserById(User.FindFirst("UserId")!.Value);
            if (user == null || (user.Role != "admin" && !user.Resources.Any(r => r.StartsWith(node.NodeId + "_")))) return Forbid();
            var token = JwtUtils.GenerateNodeToken(user, node, User, request.Scope);
            var principal = JwtUtils.ValidateNodeToken(token, node)!;
            return Ok(new ApiResponse<object> { Code = 200, Message = "节点凭据签发成功", Data = new
            {
                token, expiresAt = long.Parse(principal.FindFirst("exp")!.Value)
            }});
        }

        public MasterNodeController(ILogger<MasterNodeController> logger)
        {
            _logger = logger;
        }

        private string? ReadLinkResponse(HttpService.HttpResponse response, string stage, out JObject? body)
        {
            body = null;
            if (response.StatusCode == 0)
            {
                _logger.LogWarning(response.ResponseException as Exception, "链接子节点失败，阶段: {Stage}", stage);
                return response.ResponseException switch
                {
                    TimeoutException or OperationCanceledException => $"{stage}超时，请检查子节点地址、端口及网络连通性",
                    HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError } => $"{stage}失败：无法解析子节点域名，请检查地址和 DNS 配置",
                    HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError } => $"{stage}失败：HTTPS 连接失败，请检查子节点证书及 HTTP/HTTPS 配置",
                    _ => $"{stage}失败：无法连接子节点，请检查地址、端口是否填写正确，以及服务是否启动、网络是否可达"
                };
            }

            try { body = JObject.Parse(response.Content ?? ""); }
            catch (Newtonsoft.Json.JsonException) { /* HTTP 错误页可能是 HTML，不能误报为协议不兼容。 */ }

            if (!response.IsSuccessStatusCode)
            {
                var reason = body?["code"]?.Type == JTokenType.Integer && body["message"]?.Type == JTokenType.String
                    ? body["message"]!.Value<string>() : null;
                return $"{stage}失败（HTTP {response.StatusCode}）：" +
                    (string.IsNullOrWhiteSpace(reason) ? "请检查子节点地址、端口及反向代理配置" : reason);
            }
            if (body?["code"]?.Type != JTokenType.Integer || !int.TryParse(body["code"]!.ToString(), out var code))
                return $"{stage}失败：未收到有效的 MSLX 接口响应，请检查地址、端口及反向代理配置";
            if (code != 200)
                return $"{stage}失败：" + (body["message"]?.Type == JTokenType.String
                    ? body["message"]!.Value<string>() : $"子节点返回错误码 {code}");
            return null;
        }

        private string? ValidateNodeAddress(LinkNodeRequest request, out string masterUrl)
        {
            masterUrl = !string.IsNullOrEmpty(request.MasterUrl) ? request.MasterUrl : $"{Request.Scheme}://{Request.Host}";
            if (!Uri.TryCreate(request.NodeUrl, UriKind.Absolute, out var nodeUri) ||
                nodeUri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(nodeUri.UserInfo) ||
                !string.IsNullOrEmpty(nodeUri.Query) || !string.IsNullOrEmpty(nodeUri.Fragment))
                return "节点地址必须为有效 HTTP/HTTPS 地址";
            if (nodeUri.Scheme == "http" && (Request.IsHttps ||
                (Uri.TryCreate(masterUrl, UriKind.Absolute, out var masterUri) && masterUri.Scheme == "https")))
                return "HTTPS 主节点不能连接 HTTP 子节点，请为子节点配置 HTTPS 后重试";
            request.NodeUrl = nodeUri.AbsoluteUri.TrimEnd('/');
            return null;
        }

        [Authorize(Roles = "admin")]
        [HttpPost("link")]
        public async Task<IActionResult> LinkSlaveNode([FromBody] LinkNodeRequest request)
        {
            bool isSlaveMode = bool.Parse(IConfigBase.Config.ReadConfigKey("IsSlaveMode")?.ToString() ?? "false");
            if (isSlaveMode) return BadRequest(new ApiResponse<object> { Code = 400, Message = "当前为主节点专有接口，子节点模式下不可用" });

            var addressError = ValidateNodeAddress(request, out var masterUrl);
            if (addressError != null) return BadRequest(new ApiResponse<object> { Code = 400, Message = addressError });
            if (!Guid.TryParseExact(request.PairingId, "N", out _)) return BadRequest(new ApiResponse<object> { Code = 400, Message = "配对标识无效，请重新打开链接窗口" });
            if (string.IsNullOrWhiteSpace(request.LinkKey)) return BadRequest(new ApiResponse<object> { Code = 400, Message = "配对密钥不能为空" });
            await LinkLock.WaitAsync(HttpContext.RequestAborted);
            try
            {
                if (IConfigBase.NodeList.GetAllNodes().Any(n => string.Equals(n.NodeUrl.TrimEnd('/'), request.NodeUrl, StringComparison.OrdinalIgnoreCase)))
                    return Conflict(new ApiResponse<object> { Code = 409, Message = "此地址已登记，请编辑已有关系或完成解除链接" });
                var protocol = await GeneralApi.GetAsync($"{request.NodeUrl}/api/node/protocol");
                var protocolError = ReadLinkResponse(protocol, "读取子节点协议", out var protocolBody);
                if (protocolError != null)
                    return BadRequest(new ApiResponse<object> { Code = 400, Message = protocolError });
                var version = (protocolBody!["data"] as JObject)?["protocolVersion"];
                if (version?.Type != JTokenType.Integer || !int.TryParse(version.ToString(), out var protocolVersion))
                    return BadRequest(new ApiResponse<object> { Code = 400, Message = "子节点响应缺少有效的协议号，请检查地址、端口及反向代理配置；若确认连接的是 MSLX，请升级子节点" });
                if (protocolVersion != JwtUtils.NodeProtocolVersion)
                    return BadRequest(new ApiResponse<object> { Code = 400, Message = $"子节点协议不兼容（主节点：{JwtUtils.NodeProtocolVersion}，子节点：{protocolVersion}），请将主、子节点一起升级" });
                // 本地保存失败后尝试恢复节点关系
                // 请求子节点
                string masterId = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(IConfigBase.JwtSecret), Encoding.UTF8.GetBytes(request.NodeUrl))).ToLowerInvariant()[..32];
                var payload = new
                {
                    ProtocolVersion = JwtUtils.NodeProtocolVersion,
                    MasterId = masterId,
                    PairingId = request.PairingId,
                    MasterUrl = masterUrl,
                    LinkKey = request.LinkKey
                };
                
                Volatile.Write(ref _activePairingId, request.PairingId);
                var res = await GeneralApi.PostAsync(
                    $"{request.NodeUrl.TrimEnd('/')}/api/node/accept-link", 
                    HttpService.PostContentType.Json,
                    payload
                );

                var linkError = ReadLinkResponse(res, "请求子节点配对", out var resObj);
                if (linkError != null)
                    return BadRequest(new ApiResponse<object> { Code = 400, Message = linkError });
                if (resObj!["data"] is JObject linkData)
                {
                    string nodeId = linkData["nodeId"]?.Type == JTokenType.String ? linkData["nodeId"]!.Value<string>() ?? "" : "";
                    string commsKey = linkData["commsKey"]?.Type == JTokenType.String ? linkData["commsKey"]!.Value<string>() ?? "" : "";
                    if (!string.IsNullOrWhiteSpace(nodeId) && !string.IsNullOrWhiteSpace(commsKey))
                    {
                        var slaveNode = new SlaveNodeConfig
                        {
                            NodeId = nodeId,
                            NodeName = request.NodeName,
                            NodeUrl = request.NodeUrl,
                            NodeLogo = request.NodeLogo ?? "https://www.mslmc.cn/logo.png",
                            MasterUrl = masterUrl,
                            LinkKey = string.Empty,
                            CommsKey = commsKey,
                            NodeTags = request.NodeTags ?? string.Empty,
                            LinkedAt = DateTime.Now
                        };
                        IConfigBase.NodeList.AddOrUpdateNode(slaveNode);
                        _logger.LogInformation("成功链接子节点: {NodeName} ({NodeUrl}), NodeId: {NodeId}", request.NodeName, request.NodeUrl, nodeId);
                        return Ok(new ApiResponse<object> { Code = 200, Message = "链接成功", Data = PublicNode(slaveNode) });
                    }
                }
                return BadRequest(new ApiResponse<object> { Code = 400, Message = "子节点配对响应缺少有效的节点标识或通信凭据，未保存链接，请检查子节点版本及反向代理配置" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "链接子节点时发生异常");
                return BadRequest(new ApiResponse<object> { Code = 500, Message = "链接子节点时发生异常，请查看主节点日志了解详情" });
            }
            finally { Volatile.Write(ref _activePairingId, null); LinkLock.Release(); }
        }

        [Authorize]
        [HttpGet("list")]
        public IActionResult GetNodeList()
        {
            var nodes = IConfigBase.NodeList.GetAllNodes();
            var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

            if (role == "admin")
            {
                return Ok(new ApiResponse<object> { Code = 200, Message = "获取成功", Data = nodes.Select(PublicNode).ToList() });
            }
            else
            {
                var currentUserId = User.FindFirst("UserId")?.Value;
                var user = IConfigBase.UserList.GetUserById(currentUserId);
                
                var userNodeIds = user?.Resources
                    .Where(r => r.Contains("_"))
                    .Select(r => r.Split('_')[0])
                    .Distinct()
                    .ToList() ?? new List<string>();

                var safeNodes = nodes
                    .Where(n => !n.PendingRemoval && userNodeIds.Contains(n.NodeId))
                    .Select(n => new 
                    {
                        NodeId = n.NodeId,
                        NodeName = n.NodeName,
                        NodeUrl = n.NodeUrl,
                        NodeLogo = n.NodeLogo,
                        NodeTags = n.NodeTags,
                        MasterUrl = n.MasterUrl,
                        LinkedAt = n.LinkedAt
                    }).ToList();
                    
                return Ok(new ApiResponse<object> { Code = 200, Message = "获取成功", Data = safeNodes });
            }
        }

        [Authorize(Roles = "admin")]
        [HttpPost("delete/{id}")]
        public IActionResult UnlinkNode(string id, [FromQuery] bool force = false)
        {
            var node = IConfigBase.NodeList.GetNodeById(id);
            if (node == null) return force ? Ok(new ApiResponse<object> { Code = 200, Message = "记录已移除" })
                : NotFound(new ApiResponse<object> { Code = 404, Message = "节点不存在" });
            if (force)
            {
                if (!node.PendingRemoval) return Conflict(new ApiResponse<object> { Code = 409, Message = "请先解除链接，再移除本地记录" });
                IConfigBase.NodeList.RemovePendingNode(id, node.CommsKey);
                NodeStatsManager.RemoveNodeStats(id);
                return Ok(new ApiResponse<object> { Code = 200, Message = "已从主节点移除记录" });
            }
            IConfigBase.NodeList.MarkPendingRemoval(id);
            NodeStatsManager.RemoveNodeStats(id);
            return Ok(new ApiResponse<object> { Code = 200, Message = "主节点授权已撤销，正在清理子节点链接" });
        }

        [Authorize(Roles = "admin")]
        [HttpPost("update/{id}")]
        public async Task<IActionResult> EditNode(string id, [FromBody] LinkNodeRequest request)
        {
            var existingNode = IConfigBase.NodeList.GetAllNodes().FirstOrDefault(n => n.NodeId == id);
            if (existingNode == null || existingNode.PendingRemoval) return BadRequest(new ApiResponse<object> { Code = 400, Message = "节点不存在" });

            var addressError = ValidateNodeAddress(request, out var masterUrl);
            if (addressError != null) return BadRequest(new ApiResponse<object> { Code = 400, Message = addressError });
            
            // 向子节点同步信息
            try
            {
                var payload = new { 
                    MasterUrl = masterUrl, 
                    CommsKey = existingNode.CommsKey,
                    NodeId = existingNode.NodeId
                };
                var res = await GeneralApi.PostAsync(
                    $"{request.NodeUrl.TrimEnd('/')}/api/node/update-link",
                    HttpService.PostContentType.Json,
                    payload
                );
                
                if (!res.IsSuccessStatusCode)
                {
                    _logger.LogWarning("同步配置到子节点失败: {NodeName} ({NodeUrl}), 状态码: {StatusCode}", request.NodeName, request.NodeUrl, res.StatusCode);
                    return BadRequest(new ApiResponse<object> { Code = 400, Message = "同步主节点配置到子节点失败，请检查连通性" });
                }
                _logger.LogInformation("成功同步配置到子节点: {NodeName} ({NodeUrl})", request.NodeName, request.NodeUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "同步主节点地址时发生异常");
                return BadRequest(new ApiResponse<object> { Code = 500, Message = "同步主节点配置网络异常" });
            }

            existingNode.NodeName = request.NodeName;
            existingNode.NodeUrl = request.NodeUrl;
            existingNode.NodeLogo = request.NodeLogo ?? "https://www.mslmc.cn/logo.png";
            existingNode.MasterUrl = masterUrl;
            existingNode.LinkKey = string.Empty;
            existingNode.NodeTags = request.NodeTags ?? string.Empty;

            if (!IConfigBase.NodeList.UpdateActiveNode(existingNode))
                return Conflict(new ApiResponse<object> { Code = 409, Message = "节点关系已变化，请刷新后重试" });
            return Ok(new ApiResponse<object> { Code = 200, Message = "修改成功" });
        }

        [AllowAnonymous]
        [HttpPost("verify-token")]
        public IActionResult VerifyToken([FromBody] VerifyTokenRequest request, [FromHeader(Name = "x-api-key")] string commsKey)
        {
            // 子节点用户鉴权接口，需要 commsKey （独立的通讯key，专门用于子节点找主节点通讯）
            var node = IConfigBase.NodeList.GetAllNodes().FirstOrDefault(n => n.CommsKey == commsKey);
            if (node == null)
            {
                return Unauthorized(new ApiResponse<object> { Code = 401, Message = "无效的通讯密钥" });
            }

            var principal = node.PendingRemoval ? null : JwtUtils.ValidateNodeToken(request.Token, node);
            if (principal != null)
            {
                var userId = principal.FindFirst("UserId")?.Value;
                var role = principal.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

                if (!string.IsNullOrEmpty(userId))
                {
                    var user = IConfigBase.UserList.GetUserById(userId);
                    if (user != null)
                    {
                        var rejectionReason = JwtUtils.GetTokenRejectionReason(principal, user);
                        if (rejectionReason != null)
                        {
                            return Unauthorized(new ApiResponse<object> { Code = 401, Message = rejectionReason });
                        }

                        var nodeResources = new List<string>();
                        foreach (var res in user.Resources)
                        {
                            if (res.StartsWith(node.NodeId + "_"))
                            {
                                nodeResources.Add(res.Substring(node.NodeId.Length + 1));
                            }
                        }
                        var scope = principal.FindFirst("Scope")?.Value;
                        return Ok(new ApiResponse<object> { Code = 200, Message = "鉴权成功", Data = new { userId = userId, role = user.Role, resources = nodeResources, scope = scope, expiresAt = long.Parse(principal.FindFirst("exp")!.Value) } });
                    }
                }
            }
            
            return Unauthorized(new ApiResponse<object> { Code = 401, Message = "无效的令牌" });
        }

        [AllowAnonymous]
        [HttpPost("report-stats")]
        public IActionResult ReportStats([FromBody] NodeStatsPayload stats, [FromHeader(Name = "x-api-key")] string commsKey)
        {
            var node = IConfigBase.NodeList.GetAllNodes().FirstOrDefault(n => n.CommsKey == commsKey);
            if (node == null) return Unauthorized(new ApiResponse<object> { Code = 401, Message = "无效的通讯密钥" });

            if (node.PendingRemoval) return StatusCode(410, new ApiResponse<object> { Code = 410, Message = "节点关系已撤销" });
            NodeStatsManager.UpdateNodeStats(node.NodeId, stats);
            return Ok(new ApiResponse<object> { Code = 200, Message = "上报成功" });
        }
    }
}
