using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;

namespace MSLX.Daemon.Controllers.AuthControllers;

[ApiController]
[Route("api/admin/user")]
[Authorize(Roles = "admin")]
public class AdminUserController : ControllerBase
{
    /// <summary>
    /// 查询所有用户
    /// </summary>
    [HttpGet("list")]
    public IActionResult GetList()
    {
        var users = IConfigBase.UserList.GetAllUsers();

        var dtoList = users.Select(u => new UserDto
        {
            Id = u.Id,
            Username = u.Username,
            Name = u.Name,
            Avatar = u.Avatar,
            Role = u.Role,
            ApiKey = u.ApiKey,
            LastLoginTime = u.LastLoginTime,
            Resources = u.Resources
        }).ToList();

        return Ok(new ApiResponse<List<UserDto>>
        {
            Code = 200,
            Message = "success",
            Data = dtoList
        });
    }

    /// <summary>
    /// 新建用户
    /// </summary>
    [HttpPost("create")]
    public IActionResult CreateUser([FromBody] AdminCreateUserRequest request)
    {
        // 检查用户名是否存在
        if (IConfigBase.UserList.GetUserByUsername(request.Username) != null)
        {
            return BadRequest(new ApiResponse<object> { Code = 400, Message = "用户名已存在" });
        }

        // 密码复杂度校验
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

        var newUser = new UserInfo
        {
            Username = request.Username,
            Name = request.Name,
            Role = request.Role,
            Resources = request.Resources,
            ApiKey = StringServices.GenerateRandomString(32),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Avatar = "https://www.mslmc.cn/logo.png"
        };

        if (IConfigBase.UserList.CreateUser(newUser))
        {
            return Ok(new ApiResponse<object> { Code = 200, Message = "创建成功" });
        }

        return StatusCode(500, new ApiResponse<object> { Code = 500, Message = "创建失败" });
    }

    /// <summary>
    /// 修改
    /// </summary>
    [HttpPost("update/{id}")]
    public IActionResult UpdateUser(string id, [FromBody] AdminUpdateUserRequest request)
    {
        var user = IConfigBase.UserList.GetUserById(id);
        if (user == null) return NotFound(new ApiResponse<object> { Code = 404, Message = "用户不存在" });

        // 不允许修改用户名，只能修改属性
        if (request.Name != null) user.Name = request.Name;
        if (request.Avatar != null) user.Avatar = request.Avatar;
        if (request.Role != null) user.Role = request.Role;
        if (request.Resources != null) user.Resources = request.Resources;

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

        if (IConfigBase.UserList.UpdateUser(user))
        {
            return Ok(new ApiResponse<object> { Code = 200, Message = "更新成功" });
        }

        return StatusCode(500, new ApiResponse<object> { Code = 500, Message = "保存失败" });
    }

    /// <summary>
    /// 删除
    /// </summary>
    [HttpPost("delete/{id}")]
    public IActionResult DeleteUser(string id)
    {
        // 不允许用户想不开
        var currentUserId = User.FindFirst("UserId")?.Value;
        if (currentUserId == id)
        {
            return BadRequest(new ApiResponse<object> { Code = 400, Message = "不能删除自己" });
        }

        if (IConfigBase.UserList.DeleteUser(id))
        {
            return Ok(new ApiResponse<object> { Code = 200, Message = "删除成功" });
        }

        return NotFound(new ApiResponse<object> { Code = 404, Message = "用户不存在或删除失败" });
    }
}