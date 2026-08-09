using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using MSLX.SDK.Models;

namespace MSLX.Daemon.Controllers.AuthControllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IMemoryCache _cache;

    // 登录错误次数限制
    private const int MaxErrorLimit = 10;                
    private readonly TimeSpan ErrorCountWindow = TimeSpan.FromMinutes(5);
    private readonly TimeSpan BanDuration = TimeSpan.FromMinutes(60);
    private readonly TimeSpan BrowserLaunchTokenLifetime = TimeSpan.FromSeconds(60);
    
    public AuthController(IMemoryCache cache)
    {
        _cache = cache;
    }

    /// <summary>
    /// 为 Desktop 创建一次性浏览器登录令牌。
    /// </summary>
    [HttpPost("browser-launch")]
    [Authorize(Roles = "admin")]
    public IActionResult CreateBrowserLaunchToken()
    {
        var userId = User.FindFirst("UserId")?.Value;
        var user = string.IsNullOrWhiteSpace(userId)
            ? null
            : IConfigBase.UserList.GetUserById(userId);

        if (user == null)
        {
            return Unauthorized(new ApiResponse<object>
            {
                Code = 401,
                Message = "无法确定当前管理用户。"
            });
        }

        string launchToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        string webToken = JwtUtils.GenerateToken(user);

        _cache.Set(
            $"BROWSER_LAUNCH_{launchToken}",
            webToken,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = BrowserLaunchTokenLifetime,
                Size = 1
            });

        return Ok(new ApiResponse<object>
        {
            Code = 200,
            Message = "浏览器登录令牌创建成功",
            Data = new
            {
                token = launchToken,
                expiresIn = (int)BrowserLaunchTokenLifetime.TotalSeconds
            }
        });
    }

    /// <summary>
    /// 消费一次性浏览器登录令牌，并初始化 Web Panel 的登录状态。
    /// </summary>
    [HttpGet("browser-launch")]
    [AllowAnonymous]
    public IActionResult ConsumeBrowserLaunchToken([FromQuery] string? token)
    {
        if (string.IsNullOrWhiteSpace(token) ||
            !_cache.TryGetValue($"BROWSER_LAUNCH_{token}", out string? webToken) ||
            string.IsNullOrWhiteSpace(webToken))
        {
            return Content(
                "<!doctype html><meta charset=\"utf-8\"><title>登录链接已失效</title><p>登录链接已失效，请从 MSLX Desktop 重新打开控制台。</p>",
                "text/html; charset=utf-8");
        }

        // 令牌只能使用一次，避免复制链接后重复登录。
        _cache.Remove($"BROWSER_LAUNCH_{token}");

        string serializedWebToken = JsonSerializer.Serialize(webToken);
        string html = $"""
            <!doctype html>
            <html lang="zh-CN">
            <head><meta charset="utf-8"><title>正在打开 MSLX 控制台</title></head>
            <body>
            <p>正在完成登录，请稍候...</p>
            <script>
            localStorage.setItem('mslx-web-token', {serializedWebToken});
            localStorage.setItem('mslx-base-url', window.location.origin);
            window.location.replace('/');
            </script>
            </body>
            </html>
            """;

        Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return Content(html, "text/html; charset=utf-8");
    }

    [HttpPost("login")]
    [AllowAnonymous] 
    public IActionResult Login([FromBody] LoginRequest request)
    {
        var remoteIp = HttpContext.Connection.RemoteIpAddress;
        var clientIp = remoteIp?.ToString() ?? "127.0.0.1";
        bool isLocalIp = remoteIp != null && IPAddress.IsLoopback(remoteIp);

        string banKey = $"BAN_{clientIp}";
        string countKey = $"ERR_COUNT_{clientIp}";

        // 拦截
        if (!isLocalIp && _cache.TryGetValue(banKey, out _))
        {
            return Ok(new ApiResponse<object>
            {
                Code = 403, 
                Message = $"您的 IP 已被暂时封禁，请于 {BanDuration.TotalMinutes} 分钟后再试。"
            });
        }

        // 登录流程
        if (IConfigBase.UserList.ValidateUser(request.Username, request.Password))
        {
            // 成功则清空计数
            if (!isLocalIp)
            {
                _cache.Remove(countKey);
            }

            var user = IConfigBase.UserList.GetUserByUsername(request.Username);
            IConfigBase.UserList.UpdateLastLoginTime(request.Username);

            if (user == null)
            {
                return Ok(new ApiResponse<object> { Code = 500, Message = "获取用户信息失败" });
            }

            string token = JwtUtils.GenerateToken(user);

            var resultData = new
            {
                token,
                userInfo = new
                {
                    user.Id,
                    user.Username,
                    user.Avatar,
                    user.Role,
                    user.Resources,
                    user.LastLoginTime
                }
            };

            return Ok(new ApiResponse<object>
            {
                Code = 200,
                Message = "登录成功",
                Data = resultData
            });
        }

        // 登录失败计数
        if (!isLocalIp)
        {
            _cache.TryGetValue(countKey, out int currentCount);
            currentCount++;

            if (currentCount >= MaxErrorLimit)
            {
                // banip
                _cache.Set(banKey, true, BanDuration);
                _cache.Remove(countKey);
                
                return Ok(new ApiResponse<object>
                {
                    Code = 403,
                    Message = $"密码错误次数过多，您的 IP 已被封禁 {BanDuration.TotalMinutes} 分钟。"
                });
            }
            _cache.Set(countKey, currentCount, ErrorCountWindow); // 还没到封禁次数 仅累加
        }
        
        return Ok(new ApiResponse<object>
        {
            Code = 401,
            Message = "用户名或密码错误"
        });
    }
}
