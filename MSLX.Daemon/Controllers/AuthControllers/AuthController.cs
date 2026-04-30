using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using System.Net;
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
    
    public AuthController(IMemoryCache cache)
    {
        _cache = cache;
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