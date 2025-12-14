using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using MSLX.Daemon.Models;

namespace MSLX.Daemon.Middleware;

public class CustomAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        // 403
        if (authorizeResult.Forbidden)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";

            var response = new ApiResponse<object>
            {
                Code = 403,
                Message = "权限不足：您无权访问此接口",
                Data = null
            };

            await context.Response.WriteAsJsonAsync(response);
            return;
        }

        // 401
        if (authorizeResult.Challenged)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";

            var response = new ApiResponse<object>
            {
                Code = 401,
                Message = "未授权：请先登录",
                Data = null
            };

            await context.Response.WriteAsJsonAsync(response);
            return;
        }

        // pass
        await _defaultHandler.HandleAsync(next, context, policy, authorizeResult);
    }
}