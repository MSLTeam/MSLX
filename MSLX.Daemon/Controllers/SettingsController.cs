using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models;
using MSLX.Daemon.Models.Settings;
using MSLX.Daemon.Utils;

namespace MSLX.Daemon.Controllers;

[ApiController]
[Route("api/settings")]
public class SettingsController: ControllerBase 
{
    [HttpGet]
    public IActionResult GetSettings()
    {
        return Ok(new ApiResponse<object>
            {
                Code = 200,
                Message = "获取成功",
                Data = new
                {
                    User = ConfigServices.Config.ReadConfig()["user"]?? "",
                    Avatar = ConfigServices.Config.ReadConfig()["avatar"]?? "",
                    FireWallBanLocalAddr = ConfigServices.Config.ReadConfig()["fireWallBanLocalAddr"]?? false,
                    OpenWebConsoleOnLaunch = ConfigServices.Config.ReadConfig()["openWebConsoleOnLaunch"]?? true,
                }
            }
        );
    }
    
    [HttpPost]
    public IActionResult UpdateSettings([FromBody] UpdateSettingsRequest request)
    {
        ConfigServices.Config.WriteConfigKey("user", request.User);
        ConfigServices.Config.WriteConfigKey("avatar", request.Avatar);
        ConfigServices.Config.WriteConfigKey("fireWallBanLocalAddr", request.FireWallBanLocalAddr);
        ConfigServices.Config.WriteConfigKey("openWebConsoleOnLaunch", request.OpenWebConsoleOnLaunch);
        return Ok(new ApiResponse<object>
            {
                Code = 200,
                Message = "更新成功",
            }
        );
    }
}