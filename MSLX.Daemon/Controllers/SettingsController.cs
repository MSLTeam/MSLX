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
                    FireWallBanLocalAddr = ConfigServices.Config.ReadConfig()["fireWallBanLocalAddr"]?? false,
                    OpenWebConsoleOnLaunch = ConfigServices.Config.ReadConfig()["openWebConsoleOnLaunch"]?? true,
                    NeoForgeInstallerMirrors = ConfigServices.Config.ReadConfig()["neoForgeInstallerMirrors"]?? "MSL Mirrors",
                    ListenHost = ConfigServices.Config.ReadConfig()["listenHost"]?? "localhost",
                    ListenPort = ConfigServices.Config.ReadConfig()["listenPort"]?? 1027,
                }
            }
        );
    }
    
    [HttpPost]
    public IActionResult UpdateSettings([FromBody] UpdateSettingsRequest request)
    {
        ConfigServices.Config.WriteConfigKey("fireWallBanLocalAddr", request.FireWallBanLocalAddr);
        ConfigServices.Config.WriteConfigKey("openWebConsoleOnLaunch", request.OpenWebConsoleOnLaunch);
        ConfigServices.Config.WriteConfigKey("neoForgeInstallerMirrors", request.NeoForgeInstallerMirrors);
        ConfigServices.Config.WriteConfigKey("listenHost", request.ListenHost);
        ConfigServices.Config.WriteConfigKey("listenPort", request.ListenPort);
        return Ok(new ApiResponse<object>
            {
                Code = 200,
                Message = "更新成功",
            }
        );
    }
}