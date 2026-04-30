using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.Models;

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
            OpenMSLID = u.OAuthMSLOpenID ?? "0",
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
        if (IConfigBase.UserList.GetUserByUsername(request.Username) != null)
        {
            return BadRequest(new ApiResponse<object> { Code = 400, Message = "用户名已存在" });
        }

        if (request.Password.Length < 8 || request.Password.Length > 32)
        {
            return BadRequest(new ApiResponse<object> { Code = 400, Message = "密码长度必须在 8-32 位之间" });
        }

        int score = 0;
        if (request.Password.Any(char.IsUpper)) score++; 
        if (request.Password.Any(char.IsLower)) score++; 
        if (request.Password.Any(char.IsDigit)) score++; 
        if (request.Password.Any(ch => !char.IsLetterOrDigit(ch))) score++; 

        if (score < 3)
        {
            return BadRequest(new ApiResponse<object>
            {
                Code = 400,
                Message = "密码复杂度不足：大写字母、小写字母、数字、特殊字符中至少包含三种"
            });
        }

        // 清洗前端传来的资源列表
        var validResources = ValidateAndCleanResources(request.Resources);

        var newUser = new UserInfo
        {
            Username = request.Username,
            Name = request.Name,
            Role = request.Role,
            Resources = validResources, // 清洗后的资源列表
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

        if (request.Name != null) user.Name = request.Name;
        if (request.Avatar != null) user.Avatar = request.Avatar;
        if (request.Role != null) user.Role = request.Role;
        
        // 清洗并更新资源列表
        if (request.Resources != null)
        {
            user.Resources = ValidateAndCleanResources(request.Resources);
        }

        if (!string.IsNullOrEmpty(request.Password))
        {
            if (request.Password.Length < 8 || request.Password.Length > 32)
            {
                return BadRequest(new ApiResponse<object> { Code = 400, Message = "密码长度必须在 8-32 位之间" });
            }

            int score = 0;
            if (request.Password.Any(char.IsUpper)) score++; 
            if (request.Password.Any(char.IsLower)) score++; 
            if (request.Password.Any(char.IsDigit)) score++; 
            if (request.Password.Any(ch => !char.IsLetterOrDigit(ch))) score++; 

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

    /// <summary>
    /// 辅助方法：校验并清洗资源列表，过滤掉不存在的资源
    /// </summary>
    private List<string> ValidateAndCleanResources(List<string> resources)
    {
        if (resources == null || !resources.Any()) return new List<string>();

        var validResources = new List<string>();
        foreach (var res in resources)
        {
            var parts = res.Split(':');
            if (parts.Length != 2) continue;

            string type = parts[0].ToLower();
            string idStr = parts[1];

            // 校验 Server 实例是否存在
            if (type == "server" && uint.TryParse(idStr, out uint sId))
            {
                if (IConfigBase.ServerList.GetServer(sId) != null)
                {
                    validResources.Add(res);
                }
            }
            // 校验 Frp 隧道是否存在
            else if (type == "frp" && int.TryParse(idStr, out int fId))
            {
                if (IConfigBase.FrpList.GetFrpConfig(fId) != null)
                {
                    validResources.Add(res);
                }
            }
        }

        // 去重后返回
        return validResources.Distinct().ToList();
    }
}