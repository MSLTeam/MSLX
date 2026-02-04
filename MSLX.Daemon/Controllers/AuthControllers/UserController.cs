using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;

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

        var user = IConfigBase.UserList.GetUserById(userId);
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
            Resources = user.Resources,
            OpenMSLID = user.OAuthMSLOpenID ?? "0"
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

        var user = IConfigBase.UserList.GetUserById(userId);
        if (user == null) return NotFound(new ApiResponse<object> { Code = 404, Message = "用户不存在" });

        // 基本信息
        if (request.Name != null) user.Name = request.Name;
        if (request.Avatar != null) user.Avatar = request.Avatar;
        if (request.Username != null) user.Username = request.Username;

        // 更新密码
        if (!string.IsNullOrEmpty(request.Password))
        {
            // 长度校验
            if (request.Password.Length < 8 || request.Password.Length > 32)
            {
                return BadRequest(new ApiResponse<object> { Code = 400, Message = "密码长度必须在 8-32 位之间" });
            }

            // 复杂度校验：大小写字母、数字、特殊字符 (4选3)
            int score = 0;
            if (request.Password.Any(char.IsUpper)) score++; // 包含大写字母
            if (request.Password.Any(char.IsLower)) score++; // 包含小写字母
            if (request.Password.Any(char.IsDigit)) score++; // 包含数字
            if (request.Password.Any(ch => !char.IsLetterOrDigit(ch))) score++; // 包含特殊字符

            if (score < 3)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Code = 400,
                    Message = "密码复杂度不足：大写字母、小写字母、数字、特殊字符中至少包含三种"
                });
            }

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
        }

        // 重置 ApiKey
        if (request.ResetApiKey)
        {
            user.ApiKey = StringServices.GenerateRandomString(32);
        }

        // 保存到文件
        if (IConfigBase.UserList.UpdateUser(user))
        {
            return Ok(new ApiResponse<object> { Code = 200, Message = "更新成功" });
        }
        else
        {
            return StatusCode(500, new ApiResponse<object> { Code = 500, Message = "保存失败" });
        }
    }
}