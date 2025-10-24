using MSLX.Daemon.Models;
using Newtonsoft.Json;

namespace MSLX.Daemon.Middleware
{
    public class ApiKeyMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly string _apiKey;
        private const string ApiKeyHeaderName = "x-api-key"; // 读取的请求头

        // 注入中间件
        public ApiKeyMiddleware(RequestDelegate next)
        {
            _next = next;
            // 从 appsettings.json 读取 Key
            _apiKey = "API_KEY";
        }

        // 验证核心方法
        public async Task InvokeAsync(HttpContext context)
        {
            if (!context.Request.Headers.TryGetValue(ApiKeyHeaderName, out var extractedApiKey))
            {
                await HandleErrorAsync(context, 401, "未提供 API 密钥");
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
                Code = statusCode,
                Message = message,
            };

            // 设置相应
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = statusCode;

            var jsonResponse = JsonConvert.SerializeObject(response);
            await context.Response.WriteAsync(jsonResponse);
        }
    }
}