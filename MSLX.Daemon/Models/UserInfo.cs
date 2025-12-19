using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace MSLX.Daemon.Models;

public class UserInfo
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Username { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Avatar { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = "user"; // user / admin
    public string ApiKey { get; set; } = string.Empty;
    public DateTime? LastLoginTime { get; set; }
    public List<string> Resources { get; set; } = new List<string>();

    // 辅助方法：转换为 ClaimsPrincipal
    public ClaimsPrincipal ToPrincipal(string authType)
    {
        var claims = new List<Claim>
        {
            new Claim("UserId", Id), 
            new Claim(ClaimTypes.Name, Username),
            new Claim("NickName", Name),
            new Claim(ClaimTypes.Role, Role),
            new Claim("Avatar", Avatar)
        };
        // 添加资源权限到 Claim 中，方便后续 Policy 验证
        foreach (var res in Resources)
        {
            claims.Add(new Claim("Resource", res));
        }

        var identity = new ClaimsIdentity(claims, authType);
        return new ClaimsPrincipal(identity);
    }
}

public class LoginRequest
{
    [Required(ErrorMessage = "用户名不能为空")]
    public required string Username { get; set; }

    [Required(ErrorMessage = "密码不能为空")]
    public required string Password { get; set; }
}

// --- 返回给前端的用户信息 ---
public class UserDto
{
    public string Id { get; set; }
    public string Username { get; set; }
    public string Name { get; set; }
    public string Avatar { get; set; }
    public string Role { get; set; }
    public string ApiKey { get; set; } // 用户可以看到自己的 API Key
    public DateTime? LastLoginTime { get; set; }
    public List<string> Resources { get; set; }
}

// --- 用户修改自己信息的请求 ---
public class UpdateSelfRequest
{
    public string? Username { get; set; }
    public string? Name { get; set; } // 昵称
    public string? Avatar { get; set; } // 头像
    public string? Password { get; set; } // 新密码 (可选，为空则不改)
    public bool ResetApiKey { get; set; } = false; // 是否重置 ApiKey
}

// --- 管理员创建用户请求 ---
public class AdminCreateUserRequest
{
    [Required(ErrorMessage = "用户名必填")]
    public required string Username { get; set; }
    
    [Required(ErrorMessage = "密码必填")]
    public required string Password { get; set; }
    
    public string Name { get; set; } = "";
    public string Role { get; set; } = "user"; // user 或 admin
    public List<string> Resources { get; set; } = new List<string>();
}

// --- 管理员修改用户请求 ---
public class AdminUpdateUserRequest
{
    public string? Name { get; set; }
    public string? Avatar { get; set; }
    public string? Password { get; set; } // 为空不改
    public string? Role { get; set; } // 为空不改
    public List<string>? Resources { get; set; } // 为 null 不改
    public bool ResetApiKey { get; set; } = false;
}