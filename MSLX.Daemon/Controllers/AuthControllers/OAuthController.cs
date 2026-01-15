using Downloader;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using Newtonsoft.Json.Linq;
using System.Net;

namespace MSLX.Daemon.Controllers.AuthControllers;

[ApiController]
[Route("api/auth/oauth")]
public class OAuthController : ControllerBase
{
    public class OAuthCodeRequest
    {
        public string Code { get; set; }
    }

    [HttpGet("url")]
    [AllowAnonymous]
    public IActionResult GetOAuthURL([FromQuery] string state, string callback)
    {
        try
        {
            var clientId = IConfigBase.Config.ReadConfig()["oAuthMSLClientID"]?.ToString();
            if (string.IsNullOrEmpty(clientId) || clientId.Length != 27)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Code = 400,
                    Message = "尚未配置MSL OAuth 2.0，请先前往MSLX设置中进行配置。",
                });
            }
            var oauthUrl = $"https://user.mslmc.net/oauth/authorize?response_type=code&client_id={clientId}&redirect_uri={Uri.EscapeDataString(callback)}&state={state}";
            return Ok(new ApiResponse<object>
            {
                Code = 200,
                Message = "获取成功",
                Data = new
                {
                    url = oauthUrl,
                }
            });
        }
        catch (Exception e)
        {
            return BadRequest(new ApiResponse<object>
            {
                Code = 400,
                Message = e.Message,
            });
        }
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] OAuthCodeRequest request)
    {
        try
        {
            uint uid = await GetOpenIDAsync(request.Code);
            var user = IConfigBase.UserList.GetUserByOpenId(uid.ToString());
            if(user == null)
            {
                throw new Exception("此MSL账户未绑定任何此MSLX实例的任何账户");
            }
            IConfigBase.UserList.UpdateLastLoginTime(user.Username);
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
        catch (Exception e)
        {
            return BadRequest(new ApiResponse<object>
            {
                Code = 400,
                Message = e.Message,
            });
        }
    }

    [HttpPost("bind")]
    public async Task<IActionResult> Bind([FromBody] OAuthCodeRequest request)
    {
        try
        {
            var userId = User.FindFirst("UserId")?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new ApiResponse<object> { Code = 401, Message = "未授权或Token无效" });
            }

            uint mslUid = await GetOpenIDAsync(request.Code);
            bool success = IConfigBase.UserList.BindUserOpenId(userId, mslUid.ToString());

            if (!success)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Code = 400,
                    Message = "绑定失败：用户不存在，或该MSL账户已被其他用户绑定。",
                });
            }

            return Ok(new ApiResponse<object>
            {
                Code = 200,
                Message = "绑定成功"
            });
        }
        catch (Exception e)
        {
            return BadRequest(new ApiResponse<object>
            {
                Code = 400,
                Message = e.Message,
            });
        }
    }

    [HttpPost("unbind")]
    public IActionResult Unbind()
    {
        try
        {
            var userId = User.FindFirst("UserId")?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized(new ApiResponse<object> { Code = 401, Message = "未授权或Token无效" });
            }

            // 解绑
            bool success = IConfigBase.UserList.UnbindUserOpenId(userId);

            if (!success)
            {
                return BadRequest(new ApiResponse<object>
                {
                    Code = 400,
                    Message = "解绑失败：用户不存在或发生错误",
                });
            }

            return Ok(new ApiResponse<object>
            {
                Code = 200,
                Message = "解绑成功"
            });
        }
        catch (Exception e)
        {
            return BadRequest(new ApiResponse<object>
            {
                Code = 400,
                Message = e.Message,
            });
        }
    }


    private async Task<uint> GetOpenIDAsync(string code)
    {
        // 简单检查配置
        var clientId = IConfigBase.Config.ReadConfig()["oAuthMSLClientID"]?.ToString();
        if (string.IsNullOrEmpty(clientId) || clientId.Length != 27)
        {
            throw new Exception("尚未配置MSL OAuth 2.0，请先前往MSLX设置中进行配置。");
        }
        HttpService.HttpResponse res = await MSLUser.PostAsync("/oauth/exchangeAccessToken", HttpService.PostContentType.Json, new
        {
            code,
            client_id = IConfigBase.Config.ReadConfig()["oAuthMSLClientID"]?.ToString(),
            client_secret = IConfigBase.Config.ReadConfig()["oAuthMSLClientSecret"]?.ToString(),
        });
        if (res.IsSuccessStatusCode)
        {
            JObject jobj_token = JObject.Parse(res.Content ?? "{}");
            string accessToken = jobj_token["access_token"]?.ToString() ?? "";

            if (!string.IsNullOrEmpty(accessToken))
            {
                HttpService.HttpResponse res_user = await MSLUser.GetAsync("/oauth/user", null, new Dictionary<string, string>
                {
                    ["Authorization"] = $"Bearer {accessToken}"
                });
                if (res_user.IsSuccessStatusCode)
                {
                    JObject jobj_user = JObject.Parse(res_user.Content ?? "{}");
                    uint uid = jobj_user["uid"]?.Value<uint>() ?? 0;
                    if (uid != 0)
                    {
                        return uid;
                    }
                }
                throw new Exception("MSL用户中心获取用户信息失败！");
            }
        }
        throw new Exception("MSL用户中心验证Code失败！");
    }
}

