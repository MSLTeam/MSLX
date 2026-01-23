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
                            new Claim(ClaimTypes.Name, "全局管理员"),
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

            // 结果判定
            if (isAuthenticated)
            {
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
                    authErrorMessage = "TokenExpired";
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