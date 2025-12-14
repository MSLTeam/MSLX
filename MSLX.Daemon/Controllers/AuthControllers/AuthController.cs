using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models;
using MSLX.Daemon.Utils;

namespace MSLX.Daemon.Controllers.AuthControllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous] 
    public IActionResult Login([FromBody] LoginRequest request)
    {
        if (ConfigServices.UserList.ValidateUser(request.Username, request.Password))
        {
            var user = ConfigServices.UserList.GetUserByUsername(request.Username);
            ConfigServices.UserList.UpdateLastLoginTime(request.Username);

            if (user == null)
            {
                return Ok(new ApiResponse<object> { Code = 500, Message = "获取用户信息失败" });
            }

            string token = JwtUtils.GenerateToken(user);

            var resultData = new
            {
                token = token,
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

        return Ok(new ApiResponse<object>
        {
            Code = 401,
            Message = "用户名或密码错误"
        });
    }
}