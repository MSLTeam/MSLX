using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using Newtonsoft.Json.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MSLX.Daemon.Controllers;

[ApiController]
public class AppInfoController : ControllerBase
{
    [HttpGet("api/status")]
    public IActionResult GetStatus()
    {
        // 获取中间件截取的用户名
        var currentUserId = User.FindFirst("UserId")?.Value;
        
        string displayName = "未登录用户";
        string displayAvatar = "https://www.mslmc.cn/logo.png";
        var roles = new List<string>();

        if (!string.IsNullOrEmpty(currentUserId))
        {
            var userInfo = IConfigBase.UserList.GetUserById(currentUserId);
            if (userInfo != null)
            {
                displayName = !string.IsNullOrEmpty(userInfo.Name) ? userInfo.Name : userInfo.Username;
                displayAvatar = userInfo.Avatar;

                // 处理权限
                if (string.Equals(userInfo.Role, "admin", StringComparison.OrdinalIgnoreCase))
                {
                    roles.Add("all");
                }
                else
                {
                    roles.Add("user");
                }
            }
            
            string osType;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                osType = "Windows";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                osType = "Linux";
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                osType = "macOS";
            }
            else
            {
                osType = RuntimeInformation.OSDescription;
            }

            string clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";

            var systemInfo = new JObject
            {
                ["netVersion"] = RuntimeInformation.FrameworkDescription,
                ["osType"] = osType,
                ["osVersion"] = RuntimeInformation.OSDescription,
                ["osArchitecture"] = RuntimeInformation.OSArchitecture.ToString(),
                ["hostname"] = Environment.MachineName
            };
            
            var statusData = new JObject
            {
                ["clientName"] = "MSLX Daemon",
                ["version"] = PlatFormServices.GetFormattedVersion(),
                ["id"] = currentUserId,
                ["user"] = displayName,  
                ["username"] = userInfo?.Username ?? "mslx",
                ["avatar"] = displayAvatar,
                ["roles"] = JToken.FromObject(roles),
                ["userIp"] = clientIp,
                ["serverTime"] = DateTime.Now,
                ["targetFrontendVersion"] = new JObject
                {
                    ["desktop"] = "0.0.0",
                    ["panel"] = "0.5.7"
                },
                ["systemInfo"] = systemInfo
            };

            var response = new ApiResponse<JObject>
            {
                Code = 200,
                Message = "MSLX Daemon 状态正常",
                Data = statusData
            };
            return Ok(response);

        }
        else
        {
            return BadRequest(new ApiResponse<object>()
            {
                Code = 400,
                Message = "用户信息错误",
            });
        }
    }

    [HttpGet("api/update/info")]
    public async Task<IActionResult> GetUpdateInfoAsync()
    {
        try
        {
            var localVerObj = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version("0.0.0.0");
            HttpService.HttpResponse res = await MSLApi.GetAsync("/query/update?software=MSLX", null);

            if (res.IsSuccessStatusCode)
            {
                JObject remoteJObj = JObject.Parse(res.Content ?? "{}");
                string remoteVerStr = remoteJObj["data"]?["daemonLatestVersion"]?.ToString() ?? "0.0.0";
                if (!Version.TryParse(remoteVerStr, out Version remoteVerObj))
                {
                    remoteVerObj = new Version("0.0.0.0");
                }

                // 版本归一化
                Version normalizedLocal = NormalizeVersion(localVerObj);
                Version normalizedRemote = NormalizeVersion(remoteVerObj);

                // 判定状态
                bool needUpdate = normalizedRemote > normalizedLocal;
                string status = "release"; // 默认为最新正式版

                if (needUpdate)
                {
                    status = "outdated";
                }
                else if (normalizedLocal > normalizedRemote)
                {
                    status = "beta"; // 本地版本比服务器还新，说明是测试版
                }

                var responseData = new
                {
                    needUpdate = needUpdate,
                    currentVersion = localVerObj.ToString(),
                    latestVersion = remoteVerStr,
                    status = status, // release / beta / outdated
                    log = remoteJObj["data"]?["log"]?.ToString()
                };

                return Ok(new ApiResponse<object>()
                {
                    Code = 200,
                    Message = "获取更新信息成功",
                    Data = responseData
                });
            }
            else
            {
                return BadRequest(new ApiResponse<object>()
                {
                    Code = 500,
                    Message = "获取更新信息失败: " + res.StatusCode,
                });
            }
        }
        catch (Exception ex)
        {
            return BadRequest(new ApiResponse<object>()
            {
                Code = 400,
                Message = "获取更新信息失败: " + ex.Message,
            });
        }
    }

    // === 辅助方法：版本归一化 ===
    private Version NormalizeVersion(Version v)
    {
        return new Version(
            v.Major < 0 ? 0 : v.Major,
            v.Minor < 0 ? 0 : v.Minor,
            v.Build < 0 ? 0 : v.Build,
            v.Revision < 0 ? 0 : v.Revision
        );
    }

    [HttpGet("api/update/download")]
    public async Task<IActionResult> GetUpdateDownloadLinkAsync()
    {
        try
        {
            HttpService.HttpResponse res = await MSLApi.GetAsync($"/download/update?software=MSLX&system={PlatFormServices.GetOs().Replace("MacOS","macOS")}&arch={PlatFormServices.GetOsArch().Replace("amd64","x64")}", null);
            if (res.IsSuccessStatusCode)
            {
                JObject remoteJObj = JObject.Parse(res.Content ?? "{}");
                return Ok(new ApiResponse<object>()
                {
                    Code = 200,
                    Message = "获取更新下载链接成功",
                    Data = new
                    {
                        web = remoteJObj["data"]?["web"]?.ToString() ?? "",
                        file = remoteJObj["data"]?["file"]?.ToString() ?? ""
                    }
                });
            }
            else
            {
                return BadRequest(new ApiResponse<object>()
                {
                    Code = 400,
                    Message = "获取更新下载链接失败: " + res.StatusCode,
                });
            }
        }
        catch (Exception ex)
        {
            return BadRequest(new ApiResponse<object>()
            {
                Code = 400,
                Message = "获取更新下载链接失败: " + ex.Message,
            });
        }
    }

    [HttpGet("api/ping")]
    [AllowAnonymous]
    public IActionResult Ping()
    {
        return Ok(new ApiResponse<object>
        {
            Code = 200,
            Message = "pong"
        });
    }
}