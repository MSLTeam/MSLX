using Microsoft.Extensions.Caching.Memory;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.Attributes;
using System.Net;
using MSLX.Daemon.Services.NodeServices;
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

        public async Task InvokeAsync(HttpContext context, NodeAuthorization nodeAuthorization)
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
                if (!context.Request.Query.TryGetValue("download_token", out extractedToken) &&
                    !context.Request.Query.TryGetValue("media_token", out extractedToken) &&
                    !context.Request.Query.TryGetValue("token", out extractedToken))
                {
                    context.Request.Query.TryGetValue(TokenHeaderName, out extractedToken);
                }
            }
            string token = extractedToken.ToString();
            if (path.StartsWithSegments("/api/hubs"))
            {
                var bearer = context.Request.Headers.Authorization.ToString();
                if (bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) token = bearer[7..];
                else if (context.Request.Query.TryGetValue("access_token", out var accessToken)) token = accessToken.ToString();
            }

            if (!string.IsNullOrEmpty(token))
            {
                var principal = JwtUtils.ValidateToken(token);
                if (principal != null)
                {
                    // 验证userid是否存在于本地
                    var userId = principal.FindFirst("UserId")?.Value;
                    
                    if (!string.IsNullOrEmpty(userId))
                    {
                        var dbUser = IConfigBase.UserList.GetUserById(userId);
                        if (dbUser != null)
                        {
                            var rejectionReason = JwtUtils.GetTokenRejectionReason(principal, dbUser);
                            if (rejectionReason == null)
                            {
                                context.User = principal;
                                isAuthenticated = true;
                            }
                            else
                            {
                                authErrorMessage = rejectionReason;
                                context.Items["isTokenValidButRejected"] = true;
                            }
                        }
                        else if (userId == "system-admin")
                        {
                            context.User = principal;
                            isAuthenticated = true;
                        }
                        else
                        {
                            authErrorMessage = "用户不存在或已被删除";
                        }
                    }
                }
                else
                {
                    authErrorMessage = "无效的用户令牌";
                }
            }

            // APIKEY验证
            var isNodeDelegation = path.StartsWithSegments("/api/node/session") || path.StartsWithSegments("/api/node/sync");
            if (!isAuthenticated && (!isNodeDelegation || string.IsNullOrEmpty(token)))
            {
                if (!context.Request.Headers.TryGetValue(ApiKeyHeaderName, out var extractedApiKey))
                {
                    if (!isNodeDelegation) context.Request.Query.TryGetValue(ApiKeyHeaderName, out extractedApiKey);
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
                            new Claim("UserId", "system-admin"),
                        };
                        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "ApiKey"));
                        NodeCallerAuthorization.CaptureApiKey(context.User, inputKey, true,
                            IConfigBase.UserList.GetUserById("system-admin")!.TokenVersion);
                        isAuthenticated = true;
                    }
                    // 用户级别 APIKey
                    else
                    {
                        var user = IConfigBase.UserList.GetUserByApiKey(inputKey);
                        if (user != null)
                        {
                            context.User = user.ToPrincipal("ApiKey");
                            NodeCallerAuthorization.CaptureApiKey(context.User, inputKey, false, user.TokenVersion);
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

            // 处理子节点鉴权
            bool isSlaveMode = bool.Parse(IConfigBase.Config.ReadConfigKey("IsSlaveMode")?.ToString() ?? "false");
            var nodeId = context.Request.Headers["x-node-id"].FirstOrDefault()
                ?? context.Request.Query["x-node-id"].ToString();
            if (isSlaveMode && !string.IsNullOrEmpty(nodeId))
            {
                try
                {
                    var lease = await nodeAuthorization.VerifyAsync(nodeId, token, context.RequestAborted);
                    NodeAuthorization.Apply(context, lease);
                    isAuthenticated = true;
                }
                catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
                {
                    var status = ex is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden }
                        ? 401 : 503;
                    await HandleErrorAsync(context, status, status == 503 ? "主节点暂时不可用，请稍后重试" : "节点授权已失效，请重新登录或检查节点权限");
                    return;
                }
            }

            // 结果判定
            if (isAuthenticated)
            {
                var scope = context.User?.FindFirst("Scope")?.Value;
                if (!string.IsNullOrEmpty(scope))
                {
                    bool isAllowedMethod = HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method);
                    if (!isAllowedMethod)
                    {
                        await HandleErrorAsync(context, 403, "令牌权限不足：降权凭据仅支持读取操作（GET/HEAD）。");
                        return;
                    }

                    // 1. 优先读取端点是否显式声明了允许的 Scope（支持插件及控制器特性声明）
                    var allowedScopeAttr = endpoint?.Metadata.GetMetadata<AllowTokenScopeAttribute>();

                    if (allowedScopeAttr != null)
                    {
                        if (!allowedScopeAttr.AllowedScopes.Contains(scope, StringComparer.OrdinalIgnoreCase))
                        {
                            await HandleErrorAsync(context, 403, $"令牌权限不足：当前接口仅允许 [{string.Join(", ", allowedScopeAttr.AllowedScopes)}] 凭据访问。");
                            return;
                        }
                    }
                    else
                    {
                        // 2. 默认白名单判定（宿主内置接口与存量兼容）
                        if (scope == "download")
                        {
                            bool isDownloadPath = (path.StartsWithSegments("/api/files/instance") && path.Value?.EndsWith("/download") == true)
                                || path.StartsWithSegments("/api/instance/backups/download")
                                || (path.StartsWithSegments("/api/plugin") && path.Value?.EndsWith("/download") == true);

                            if (!isDownloadPath)
                            {
                                await HandleErrorAsync(context, 403, "令牌权限不足：下载凭据仅允许用于文件下载（GET/HEAD）。");
                                return;
                            }
                        }
                        else if (scope == "media")
                        {
                            bool isMediaPath = path.StartsWithSegments("/api/instance/icon")
                                || (path.StartsWithSegments("/api/files/instance") && path.Value?.EndsWith("/thumbnail") == true);

                            if (!isMediaPath)
                            {
                                await HandleErrorAsync(context, 403, "令牌权限不足：媒体凭据仅允许用于媒体资源访问（GET/HEAD）。");
                                return;
                            }
                        }
                        else
                        {
                            await HandleErrorAsync(context, 403, "未知或不受支持的凭据权限范围。");
                            return;
                        }
                    }
                }

                if (isSlaveMode)
                {
                    var delegatedSettings = context.User.Identity?.AuthenticationType == "NodeAuth" && context.User.IsInRole("admin") &&
                        (path == "/api/settings" || path == "/api/settings/ssl" || path == "/api/settings/sync");
                    if (path.StartsWithSegments("/api/user") || path.StartsWithSegments("/api/settings") || path.StartsWithSegments("/api/admin"))
                    {
                        if (!delegatedSettings && !path.StartsWithSegments("/api/user/logout"))
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
            bool isTokenValidButRejected = context.Items["isTokenValidButRejected"] as bool? ?? false;

            if (!string.IsNullOrEmpty(token) && !isAuthenticated && !isTokenValidButRejected)
            {
                if (JwtUtils.IsTokenExpiredButTrusted(token))
                {
                    isExpiredSafe = true;
                    authErrorMessage = "登录已过期，请重新登录~";
                }
            }

            // 不是本地IP & 签名错误的Token 计入封禁拦截
            if (!isLocalIp && !isExpiredSafe && !isTokenValidButRejected)
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
