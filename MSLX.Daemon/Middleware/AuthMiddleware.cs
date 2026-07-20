using Microsoft.Extensions.Caching.Memory;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using System.Net;
using System.Security.Claims;

namespace MSLX.Daemon.Middleware
{
    public class AuthMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IMemoryCache _cache;

        private const string TokenHeaderName = "x-user-token";
        private const string ApiKeyHeaderName = "x-api-key";

        private const int MaxErrorLimit = 10;                
        private readonly TimeSpan ErrorCountWindow = TimeSpan.FromMinutes(5);
        private readonly TimeSpan BanDuration = TimeSpan.FromMinutes(60);

        public AuthMiddleware(RequestDelegate next, IMemoryCache cache)
        {
            _next = next;
            _cache = cache;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // 忽略非 API 请求
            var path = context.Request.Path;
            if (!path.StartsWithSegments("/api")) 
            {
                await _next(context);
                return;
            }
            // AllowAnonymous 放行
            var endpoint = context.GetEndpoint();
            if (endpoint?.Metadata?.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>() != null)
            {
                await _next(context);
                return;
            }

            // IP 安全检查
            var remoteIp = context.Connection.RemoteIpAddress;
            var clientIp = remoteIp?.ToString() ?? "127.0.0.1";
            bool isLocalIp = remoteIp != null && IPAddress.IsLoopback(remoteIp);
            
            string banKey = $"BAN_{clientIp}";
            string countKey = $"ERR_COUNT_{clientIp}";

            if (!isLocalIp && _cache.TryGetValue(banKey, out _))
            {
                await HandleErrorAsync(context, 403, $"您的 IP 已被暂时封禁，请于 {BanDuration.TotalMinutes} 分钟后再试。");
                return;
            }

            bool isAuthenticated = false;
            string authErrorMessage = "未授权";

            // Token 验证
            if (!context.Request.Headers.TryGetValue(TokenHeaderName, out var extractedToken))
            {
                context.Request.Query.TryGetValue(TokenHeaderName, out extractedToken);
            }
            string token = extractedToken.ToString();

            if (!string.IsNullOrEmpty(token))
            {
                var principal = JwtUtils.ValidateToken(token);
                if (principal != null)
                {
                    // 验证userid是否存在于本地
                    var userId = principal.FindFirst("UserId")?.Value;
                    
                    if (!string.IsNullOrEmpty(userId) && IConfigBase.UserList.GetUserById(userId) != null)
                    {
                        context.User = principal;
                        isAuthenticated = true;
                    }
                    else
                    {
                        authErrorMessage = "用户不存在或已被删除";
                    }
                }
                else
                {
                    authErrorMessage = "无效的用户令牌";
                }
            }

            // APIKEY验证
            if (!isAuthenticated)
            {
                if (!context.Request.Headers.TryGetValue(ApiKeyHeaderName, out var extractedApiKey))
                {
                    context.Request.Query.TryGetValue(ApiKeyHeaderName, out extractedApiKey);
                }

                string inputKey = extractedApiKey.ToString();

                if (!string.IsNullOrEmpty(inputKey))
                {
                    // 全局APIKey
                    string globalApiKey = IConfigBase.Config.ReadConfigKey("apiKey")?.ToString() ?? "";
                    
                    if (!string.IsNullOrEmpty(globalApiKey) && globalApiKey.Equals(inputKey))
                    {
                        var claims = new List<Claim> { 
                            new Claim(ClaimTypes.Name, "MSLX Manager"),
                            new Claim(ClaimTypes.Role, "admin"),
                            new Claim("UserId", "system-admin") 
                        };
                        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "ApiKey"));
                        isAuthenticated = true;
                    }
                    // 用户级别 APIKey
                    else
                    {
                        var user = IConfigBase.UserList.GetUserByApiKey(inputKey);
                        if (user != null)
                        {
                            context.User = user.ToPrincipal("ApiKey");
                            isAuthenticated = true;
                        }
                        else
                        {
                            authErrorMessage = "无效的 API 密钥";
                        }
                    }
                }
                else if (string.IsNullOrEmpty(token))
                {
                    authErrorMessage = "未提供认证凭证";
                }
            }

            // 子节点模式下鉴权
            bool isSlaveMode = bool.Parse(IConfigBase.Config.ReadConfigKey("IsSlaveMode")?.ToString() ?? "false");
            if (!isAuthenticated && isSlaveMode && !string.IsNullOrEmpty(token))
            {
                if (!context.Request.Headers.TryGetValue("x-node-id", out var extractedNodeId))
                {
                    context.Request.Query.TryGetValue("x-node-id", out extractedNodeId);
                }
                string nodeId = extractedNodeId.ToString();

                if (!string.IsNullOrEmpty(nodeId))
                {
                    var masterNode = IConfigBase.MasterNodes.GetMasterById(nodeId);
                    if (masterNode != null)
                    {
                        string cacheKey = $"NODE_AUTH_{nodeId}_{token}";
                        if (_cache.TryGetValue(cacheKey, out ClaimsPrincipal? cachedPrincipal) && cachedPrincipal != null)
                        {
                            context.User = cachedPrincipal;
                            isAuthenticated = true;
                        }
                        else
                        {
                            // 找主人进行鉴权喵~
                            try
                            {
                                using var client = new HttpClient();
                                client.DefaultRequestHeaders.Add("x-api-key", masterNode.CommsKey);
                                var reqContent = new StringContent(System.Text.Json.JsonSerializer.Serialize(new { Token = token }), System.Text.Encoding.UTF8, "application/json");
                                var res = await client.PostAsync($"{masterNode.MasterUrl.TrimEnd('/')}/api/node/verify-token", reqContent);
                                
                                if (res.IsSuccessStatusCode)
                                {
                                    var resStr = await res.Content.ReadAsStringAsync();
                                    var resObj = Newtonsoft.Json.Linq.JObject.Parse(resStr);
                                    if ((int?)resObj["code"] == 200)
                                    {
                                        var role = resObj["data"]?["role"]?.ToString() ?? "user";
                                        var uid = resObj["data"]?["userId"]?.ToString() ?? "";
                                        var resources = resObj["data"]?["resources"] as Newtonsoft.Json.Linq.JArray ?? new Newtonsoft.Json.Linq.JArray();
                                        
                                        var claims = new List<Claim> { 
                                            new Claim(ClaimTypes.Role, role),
                                            new Claim("UserId", uid) 
                                        };
                                        var proxyPrincipal = new ClaimsPrincipal(new ClaimsIdentity(claims, "NodeAuth"));
                                        context.User = proxyPrincipal;
                                        isAuthenticated = true;

                                        // 同步数据到子节点用户信息（影子用户）
                                        IConfigBase.UserList.UpdateShadowUser(uid, role, resources);

                                        _cache.Set(cacheKey, proxyPrincipal, TimeSpan.FromMinutes(5));
                                    }
                                    else
                                    {
                                        authErrorMessage = "主节点拒绝了该令牌";
                                    }
                                }
                                else
                                {
                                    authErrorMessage = "无法连接到主节点进行鉴权";
                                }
                            }
                            catch (Exception)
                            {
                                authErrorMessage = "向主节点鉴权时发生网络错误";
                            }
                        }
                    }
                    else
                    {
                        authErrorMessage = "未找到对应的主节点配置";
                    }
                }
            }

            // 结果判定
            if (isAuthenticated)
            {
                // 拦截一些API （这里后续可能还是需要开放，先拦着吧）
                if (isSlaveMode)
                {
                    if (path.StartsWithSegments("/api/user") || path.StartsWithSegments("/api/settings"))
                    {
                        if (!path.StartsWithSegments("/api/node"))
                        {
                            await HandleErrorAsync(context, 403, "当前为子节点模式，禁止直接修改全局配置和用户数据。");
                            return;
                        }
                    }
                }

                _cache.Remove(countKey);
                await _next(context);
                return;
            }

            // 验证token是否只是过期了
            bool isExpiredSafe = false;
            if (!string.IsNullOrEmpty(token) && !isAuthenticated)
            {
                if (JwtUtils.IsTokenExpiredButTrusted(token))
                {
                    isExpiredSafe = true;
                    authErrorMessage = "登录已过期，请重新登录~";
                }
            }

            // 不是本地IP & 签名错误的Token 计入封禁拦截
            if (!isLocalIp && !isExpiredSafe)
            {
                await ProcessFailureAsync(clientIp, countKey, banKey);
            }

            
            await HandleErrorAsync(context, 401, authErrorMessage);
        }

        private Task ProcessFailureAsync(string clientIp, string countKey, string banKey)
        {
            _cache.TryGetValue(countKey, out int currentCount);
            currentCount++;

            if (currentCount >= MaxErrorLimit)
            {
                _cache.Set(banKey, true, BanDuration);
                _cache.Remove(countKey); 
            }
            else
            {
                _cache.Set(countKey, currentCount, ErrorCountWindow);
            }
            
            return Task.CompletedTask;
        }

        private async Task HandleErrorAsync(HttpContext context, int statusCode, string message)
        {
            var response = new 
            {
                Code = statusCode,
                Message = message
            };

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = statusCode;
            await context.Response.WriteAsJsonAsync(response);
        }
    }
}