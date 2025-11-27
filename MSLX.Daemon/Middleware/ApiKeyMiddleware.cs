using Microsoft.Extensions.Caching.Memory;
using MSLX.Daemon.Models;
using MSLX.Daemon.Utils;
using System.Net; // 需引入

namespace MSLX.Daemon.Middleware
{
    public class ApiKeyMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IMemoryCache _cache;
        private readonly string _apiKey;
        private const string ApiKeyHeaderName = "x-api-key";

        // 配置拦截参数
        private const int MaxErrorLimit = 10;                // 最大错误次数
        private readonly TimeSpan ErrorCountWindow = TimeSpan.FromMinutes(5); // 统计时间窗口
        private readonly TimeSpan BanDuration = TimeSpan.FromMinutes(60);     // 封禁时长
        // ----------------

        public ApiKeyMiddleware(RequestDelegate next, IMemoryCache cache)
        {
            _next = next;
            _cache = cache;
            _apiKey = ConfigServices.Config.ReadConfigKey("api-key")?.ToString() ?? "";
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // 获取 IP 对象
            var remoteIp = context.Connection.RemoteIpAddress;
            var clientIp = remoteIp?.ToString() ?? "127.0.0.1";

            // 是否本地回环地址
            bool isLocalIp = remoteIp != null && IPAddress.IsLoopback(remoteIp);
            
            // 定义缓存 Key
            string banKey = $"BAN_{clientIp}";
            string countKey = $"ERR_COUNT_{clientIp}";

            // IP是否已被封？ (如果是本地 IP，直接跳过此检查)
            if (!isLocalIp && _cache.TryGetValue(banKey, out _))
            {
                // 你被封了～
                await HandleErrorAsync(context, 403, $"您的 IP 已被暂时封禁，请于 {BanDuration.TotalMinutes} 分钟后再试。");
                return;
            }

            // 校验APIKey
            if (!context.Request.Headers.TryGetValue(ApiKeyHeaderName, out var extractedApiKey))
            {
                if (!context.Request.Query.TryGetValue(ApiKeyHeaderName, out extractedApiKey))
                {
                    // 非本地回环地址才记录失败次数
                    if (!isLocalIp) 
                    {
                        await ProcessFailureAsync(clientIp, countKey, banKey); // 记录失败
                    }
                    await HandleErrorAsync(context, 401, "未提供 API 密钥");
                    return;
                }
            }

            // 校验配置文件
            if (string.IsNullOrEmpty(_apiKey))
            {
                await HandleErrorAsync(context, 500, "API 密钥未配置 (服务端错误)");
                return;
            }

            // 校验APIKey
            if (!_apiKey.Equals(extractedApiKey))
            {
                // 只有非本地 IP 才记录失败次数
                if (!isLocalIp)
                {
                    await ProcessFailureAsync(clientIp, countKey, banKey); // 记录失败
                }
                await HandleErrorAsync(context, 401, "API 密钥无效");
                return;
            }

            // 验证通过 否清除错误计数
            _cache.Remove(countKey);
            
            await _next(context);
        }

        /// <summary>
        /// 处理失败逻辑：增加计数，判断是否需要封禁
        /// </summary>
        private Task ProcessFailureAsync(string clientIp, string countKey, string banKey)
        {
            // 获取当前错误次数，如果不存在则为 0
            _cache.TryGetValue(countKey, out int currentCount);
            
            currentCount++;

            if (currentCount >= MaxErrorLimit)
            {
                // 超过阈值，设置封禁 Key
                _cache.Set(banKey, true, BanDuration);
                
                // 移除计数器
                _cache.Remove(countKey); 
            }
            else
            {
                // 未超阈值，更新计数器
                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(ErrorCountWindow); 
                
                _cache.Set(countKey, currentCount, ErrorCountWindow);
            }
            
            return Task.CompletedTask;
        }

        private async Task HandleErrorAsync(HttpContext context, int statusCode, string message)
        {
            var response = new ApiResponse<object>
            {
                Code = statusCode,
                Message = message,
            };

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = statusCode;
            await context.Response.WriteAsJsonAsync(response);
        }
    }
}