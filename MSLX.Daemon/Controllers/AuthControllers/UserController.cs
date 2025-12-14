using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models;
using MSLX.Daemon.Utils;

namespace MSLX.Daemon.Controllers.AuthControllers;

[ApiController]
[Route("api/user")]
public class UserController : ControllerBase
{
    /// <summary>
    /// 获取当前用户信息
    /// </summary>
    [HttpGet("me")]
    public IActionResult GetSelfInfo()
    {
        var userId = User.FindFirst("UserId")?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var user = ConfigServices.UserList.GetUserById(userId);
        if (user == null) return NotFound(new ApiResponse<object> { Code = 404, Message = "用户不存在" });
        
        var dto = new UserDto
        {
            Id = user.Id,
            Username = user.Username,
            Name = user.Name,
            Avatar = user.Avatar,
            Role = user.Role,
            ApiKey = user.ApiKey,
            LastLoginTime = user.LastLoginTime,
            Resources = user.Resources
        };

        return Ok(new ApiResponse<UserDto> { Code = 200, Message = "success", Data = dto });
    }

    /// <summary>
    /// 修改当前用户信息
    /// </summary>
    [HttpPost("me/update")]
    public IActionResult UpdateSelfInfo([FromBody] UpdateSelfRequest request)
    {
        var userId = User.FindFirst("UserId")?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var user = ConfigServices.UserList.GetUserById(userId);
        if (user == null) return NotFound(new ApiResponse<object> { Code = 404, Message = "用户不存在" });

        // 基本信息
        if (request.Name != null) user.Name = request.Name;
        if (request.Avatar != null) user.Avatar = request.Avatar;

        // 更新密码
        if (!string.IsNullOrEmpty(request.Password))
        {
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
        }

        // 重置 ApiKey
        if (request.ResetApiKey)
        {
            user.ApiKey = StringServices.GenerateRandomString(32);
        }

        // 保存到文件
        if (ConfigServices.UserList.UpdateUser(user))
        {
            return Ok(new ApiResponse<object> { Code = 200, Message = "更新成功" });
        }
        else
        {
            return StatusCode(500, new ApiResponse<object> { Code = 500, Message = "保存失败" });
        }
    }
}