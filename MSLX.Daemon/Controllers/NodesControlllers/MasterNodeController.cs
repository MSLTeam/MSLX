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
    public class MasterNodeController : ControllerBase
    {
        private readonly ILogger<MasterNodeController> _logger;

        public MasterNodeController(ILogger<MasterNodeController> logger)
        {
            _logger = logger;
        }

        [Authorize(Roles = "admin")]
        [HttpPost("link")]
        public async Task<IActionResult> LinkSlaveNode([FromBody] LinkNodeRequest request)
        {
            bool isSlaveMode = bool.Parse(IConfigBase.Config.ReadConfigKey("IsSlaveMode")?.ToString() ?? "false");
            if (isSlaveMode) return BadRequest(new ApiResponse<object> { Code = 400, Message = "当前为主节点专有接口，子节点模式下不可用" });

            try
            {
                // 请求子节点
                string masterId = Guid.NewGuid().ToString("N");
                string masterUrl = !string.IsNullOrEmpty(request.MasterUrl) ? request.MasterUrl : $"{Request.Scheme}://{Request.Host}";
                var payload = new
                {
                    MasterId = masterId,
                    MasterUrl = masterUrl,
                    LinkKey = request.LinkKey
                };
                
                var res = await GeneralApi.PostAsync(
                    $"{request.NodeUrl.TrimEnd('/')}/api/node/accept-link", 
                    HttpService.PostContentType.Json,
                    payload
                );

                if (res.IsSuccessStatusCode && !string.IsNullOrEmpty(res.Content))
                {
                    var resObj = Newtonsoft.Json.Linq.JObject.Parse(res.Content);
                    if ((int?)resObj["code"] == 200)
                    {
                        string nodeId = resObj["data"]?["nodeId"]?.ToString() ?? "";
                        string commsKey = resObj["data"]?["commsKey"]?.ToString() ?? "";

                        var slaveNode = new SlaveNodeConfig
                        {
                            NodeId = nodeId,
                            NodeName = request.NodeName,
                            NodeUrl = request.NodeUrl,
                            NodeLogo = request.NodeLogo ?? "https://www.mslmc.cn/logo.png",
                            MasterUrl = masterUrl,
                            LinkKey = request.LinkKey,
                            CommsKey = commsKey,
                            NodeTags = request.NodeTags ?? string.Empty,
                            LinkedAt = DateTime.Now
                        };
                        IConfigBase.NodeList.AddOrUpdateNode(slaveNode);
                        _logger.LogInformation("成功链接子节点: {NodeName} ({NodeUrl}), NodeId: {NodeId}", request.NodeName, request.NodeUrl, nodeId);
                        return Ok(new ApiResponse<object> { Code = 200, Message = "链接成功", Data = slaveNode });
                    }
                    else
                    {
                        _logger.LogWarning("链接子节点失败: {NodeName} ({NodeUrl}), 子节点拒绝链接: {Message}", request.NodeName, request.NodeUrl, resObj["message"]);
                        return BadRequest(new ApiResponse<object> { Code = 400, Message = $"子节点拒绝链接: {resObj["message"]}" });
                    }
                }
                _logger.LogWarning("链接子节点失败: {NodeName} ({NodeUrl}), 无法连接到子节点", request.NodeName, request.NodeUrl);
                return BadRequest(new ApiResponse<object> { Code = 400, Message = "无法连接到子节点" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "链接子节点时发生异常");
                return BadRequest(new ApiResponse<object> { Code = 500, Message = "发生网络异常: " + ex.Message });
            }
        }

        [Authorize]
        [HttpGet("list")]
        public IActionResult GetNodeList()
        {
            var nodes = IConfigBase.NodeList.GetAllNodes();
            var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

            if (role == "admin")
            {
                return Ok(new ApiResponse<List<SlaveNodeConfig>> { Code = 200, Message = "获取成功", Data = nodes });
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
                    .Where(n => userNodeIds.Contains(n.NodeId))
                    .Select(n => new 
                    {
                        NodeId = n.NodeId,
                        NodeName = n.NodeName,
                        NodeUrl = n.NodeUrl,
                        NodeLogo = n.NodeLogo,
                        MasterUrl = n.MasterUrl,
                        LinkedAt = n.LinkedAt
                    }).ToList();
                    
                return Ok(new ApiResponse<object> { Code = 200, Message = "获取成功", Data = safeNodes });
            }
        }

        [Authorize(Roles = "admin")]
        [HttpPost("delete/{id}")]
        public IActionResult UnlinkNode(string id)
        {
            if (IConfigBase.NodeList.RemoveNode(id))
            {
                NodeStatsManager.RemoveNodeStats(id);
                return Ok(new ApiResponse<object> { Code = 200, Message = "删除成功" });
            }
            return BadRequest(new ApiResponse<object> { Code = 400, Message = "节点不存在" });
        }

        [Authorize(Roles = "admin")]
        [HttpPost("update/{id}")]
        public async Task<IActionResult> EditNode(string id, [FromBody] LinkNodeRequest request)
        {
            var existingNode = IConfigBase.NodeList.GetAllNodes().FirstOrDefault(n => n.NodeId == id);
            if (existingNode == null) return BadRequest(new ApiResponse<object> { Code = 400, Message = "节点不存在" });

            string masterUrl = !string.IsNullOrEmpty(request.MasterUrl) ? request.MasterUrl : $"{Request.Scheme}://{Request.Host}";
            
            // 始终向子节点同步，不仅更新 MasterUrl，顺便下发 NodeId 修复老版本因子节点缺少 NodeId 导致的鉴权失败
            try
            {
                var payload = new { 
                    MasterUrl = masterUrl, 
                    CommsKey = existingNode.CommsKey,
                    NodeId = existingNode.NodeId // 带着 NodeId 一起过去，子节点可以顺便自愈
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
            existingNode.LinkKey = request.LinkKey;
            existingNode.NodeTags = request.NodeTags ?? string.Empty;

            IConfigBase.NodeList.AddOrUpdateNode(existingNode);
            return Ok(new ApiResponse<object> { Code = 200, Message = "修改成功" });
        }

        [AllowAnonymous]
        [HttpPost("verify-token")]
        public IActionResult VerifyToken([FromBody] VerifyTokenRequest request, [FromHeader(Name = "x-api-key")] string commsKey)
        {
            // 这是给子节点调用的，子节点会传 commsKey，我们需要校验这个 commsKey 是否存在于任何已链接的子节点配置中
            var node = IConfigBase.NodeList.GetAllNodes().FirstOrDefault(n => n.CommsKey == commsKey);
            if (node == null)
            {
                return Unauthorized(new ApiResponse<object> { Code = 401, Message = "无效的通讯密钥" });
            }

            var principal = JwtUtils.ValidateToken(request.Token);
            if (principal != null)
            {
                var userId = principal.FindFirst("UserId")?.Value;
                var role = principal.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

                if (!string.IsNullOrEmpty(userId))
                {
                    var user = IConfigBase.UserList.GetUserById(userId);
                    if (user != null)
                    {
                        var nodeResources = new List<string>();
                        foreach (var res in user.Resources)
                        {
                            if (res.StartsWith(node.NodeId + "_"))
                            {
                                nodeResources.Add(res.Substring(node.NodeId.Length + 1));
                            }
                        }
                        return Ok(new ApiResponse<object> { Code = 200, Message = "鉴权成功", Data = new { userId = userId, role = user.Role, resources = nodeResources } });
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

            NodeStatsManager.UpdateNodeStats(node.NodeId, stats);
            return Ok(new ApiResponse<object> { Code = 200, Message = "上报成功" });
        }
    }
}
