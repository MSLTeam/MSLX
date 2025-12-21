using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;

namespace MSLX.Daemon.Controllers.AuthControllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous] 
    public IActionResult Login([FromBody] LoginRequest request)
    {
        if (IConfigBase.UserList.ValidateUser(request.Username, request.Password))
        {
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

        return Ok(new ApiResponse<object>
        {
            Code = 401,
            Message = "用户名或密码错误"
        });
    }
}