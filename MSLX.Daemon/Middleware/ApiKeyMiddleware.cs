using MSLX.Daemon.Models;
using MSLX.Daemon.Utils;
using Newtonsoft.Json;

namespace MSLX.Daemon.Middleware
{
    public class ApiKeyMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly string _apiKey;
        private const string ApiKeyHeaderName = "x-api-key"; // 读取的请求头(和查询参数)

        // 注入中间件
        public ApiKeyMiddleware(RequestDelegate next)
        {
            _next = next;
            _apiKey = ConfigServices.Config.ReadConfigKey("api-key")?.ToString() ?? "";
        }

        // 验证核心方法
        public async Task InvokeAsync(HttpContext context)
        {
            if (!context.Request.Headers.TryGetValue(ApiKeyHeaderName, out var extractedApiKey))
            {
                // 如果 Header 没有，则尝试从 Query String 读取
                if (!context.Request.Query.TryGetValue(ApiKeyHeaderName, out extractedApiKey))
                {
                    await HandleErrorAsync(context, 401, "未提供 API 密钥");
                    return;
                }
            }


            if (String.IsNullOrEmpty(_apiKey))
            {
                await HandleErrorAsync(context, 500, "API 密钥未配置");
                return;
            }

            if (!_apiKey.Equals(extractedApiKey))
            {
                await HandleErrorAsync(context, 401, "API 密钥无效");
                return;
            }

            await _next(context);
        }

        // 返回信息的格式化方法
        private async Task HandleErrorAsync(HttpContext context, int statusCode, string message)
        {
            var response = new ApiResponse<object>
            {
                code = statusCode,
                message = message,
            };

            // 设置相应
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = statusCode;

            var jsonResponse = JsonConvert.SerializeObject(response);
            await context.Response.WriteAsync(jsonResponse);
        }
    }
}
