using Microsoft.IdentityModel.Tokens;
using MSLX.Daemon.Models;
using MSLX.Daemon.Utils.ConfigUtils;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

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
            };

        foreach (var res in user.Resources)
        {
            claims.Add(new Claim("Resource", res));
        }

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