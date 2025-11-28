using System.Net;
using MSLX.Daemon.Utils;

namespace MSLX.Daemon.Middleware;

public class BlockLoopbackMiddleware
{
    private readonly RequestDelegate _next;

    public BlockLoopbackMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var remoteIp = context.Connection.RemoteIpAddress ?? IPAddress.Loopback;

        if (IPAddress.IsLoopback(remoteIp) && ((bool?)ConfigServices.Config.ReadConfig()["fireWallBanLocalAddr"] ?? false))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";

            var response = new
            {
                code = 403,
                message = "Access from localhost is forbidden. (禁止本机回环访问)"
            };

            await context.Response.WriteAsJsonAsync(response);

            // 拦截请求
            return;
        }

        await _next(context);
    }
}