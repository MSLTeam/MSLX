using Microsoft.IdentityModel.Tokens;
using MSLX.Daemon.Utils.ConfigUtils;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MSLX.SDK.Models;

namespace MSLX.Daemon.Utils;

public static class JwtUtils
{
    // 生成 Token
    public static string GenerateToken(UserInfo user)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(IConfigBase.JwtSecret);

        var claims = new List<Claim>
            {
                new Claim("UserId", user.Id),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim("TokenVersion", user.TokenVersion.ToString()),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddDays(1),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    // 验证 Token
    public static ClaimsPrincipal? ValidateToken(string token)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.ASCII.GetBytes(IConfigBase.JwtSecret);
        try
        {
            var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = false,
                ValidateAudience = false,
                ClockSkew = TimeSpan.Zero // 立即过期，不留缓冲时间
            }, out SecurityToken validatedToken);

            return principal;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 验证令牌是否因登出被注销，或因修改密码/权限等事件版本过期
    /// </summary>
    public static string? GetTokenRejectionReason(ClaimsPrincipal principal, UserInfo user)
    {
        var jti = principal.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti)?.Value;
        var tokenVersionStr = principal.FindFirst("TokenVersion")?.Value;

        if (!string.IsNullOrEmpty(jti) && user.RevokedTokens != null && user.RevokedTokens.TryGetValue(jti, out var exp) && exp > DateTime.UtcNow)
        {
            return "登录状态已失效，请重新登录";
        }

        if (int.TryParse(tokenVersionStr, out int tokenVersion) && tokenVersion == user.TokenVersion)
        {
            return null; // 有效
        }

        return "登录状态已失效，请重新登录";
    }

    // 验证token合法性但过期的情况
    public static bool IsTokenExpiredButTrusted(string token)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();

            // 格式检查
            if (!tokenHandler.CanReadToken(token)) return false;

            var key = Encoding.ASCII.GetBytes(IConfigBase.JwtSecret);

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = false,
                ValidateAudience = false,

                // 不让系统自动验证时间
                ValidateLifetime = false,
                ClockSkew = TimeSpan.Zero
            };

            // 验证签名
            tokenHandler.ValidateToken(token, validationParameters, out SecurityToken validatedToken);

            // 手动检查是否过期
            if (validatedToken.ValidTo < DateTime.UtcNow)
            {
                return true;
            }

            return false; // 签名对且没过期？这咋可能哇！
        }
        catch
        {
            return false; // 来找茬的！
        }
    }
}