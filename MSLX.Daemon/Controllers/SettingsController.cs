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

    [HttpGet("webpanel/style")]
    public IActionResult GetWebPanelStyle()
    {
        return Ok(new ApiResponse<object>
        {
            Code = 200,
            Message = "获取成功",
            Data = new
            {
                WebPanelStyleLightBackground = ConfigServices.Config.ReadConfig()["webPanelStyleLightBackground"]?? "",
                WebPanelStyleDarkBackground = ConfigServices.Config.ReadConfig()["webPanelStyleDarkBackground"]?? "",
                WebPanelStyleLightBackgroundOpacity = ConfigServices.Config.ReadConfig()["webPanelStyleLightBackgroundOpacity"]?? 1.0,
                WebPanelStyleDarkBackgroundOpacity = ConfigServices.Config.ReadConfig()["webPanelStyleDarkBackgroundOpacity"]?? 1.0,
                WebPanelStyleLightComponentsOpacity = ConfigServices.Config.ReadConfig()["webPanelStyleLightComponentsOpacity"]?? 0.4,
                WebPanelStyleDarkComponentsOpacity = ConfigServices.Config.ReadConfig()["webPanelStyleDarkComponentsOpacity"]?? 0.6,
                WebpPanelTerminalBlurLight = ConfigServices.Config.ReadConfig()["webpPanelTerminalBlurLight"]?? 5.0,
                WebpPanelTerminalBlurDark = ConfigServices.Config.ReadConfig()["webpPanelTerminalBlurDark"]?? 5.0,
            }
        });
    }

    [HttpPost("webpanel/style")]
    public IActionResult UpdateWebPanelStyle([FromBody] UpdateWebPanelStyleSettingsRequest request)
    {
        ConfigServices.Config.WriteConfigKey("webPanelStyleLightBackground",request.WebPanelStyleLightBackground);
        ConfigServices.Config.WriteConfigKey("webPanelStyleDarkBackground",request.WebPanelStyleDarkBackground);
        ConfigServices.Config.WriteConfigKey("webPanelStyleLightBackgroundOpacity",request.WebPanelStyleLightBackgroundOpacity);
        ConfigServices.Config.WriteConfigKey("webPanelStyleDarkBackgroundOpacity",request.WebPanelStyleDarkBackgroundOpacity);
        ConfigServices.Config.WriteConfigKey("webPanelStyleLightComponentsOpacity",request.WebPanelStyleLightComponentsOpacity);
        ConfigServices.Config.WriteConfigKey("webPanelStyleDarkComponentsOpacity",request.WebPanelStyleDarkComponentsOpacity);
        ConfigServices.Config.WriteConfigKey("webpPanelTerminalBlurLight",request.WebpPanelTerminalBlurLight);
        ConfigServices.Config.WriteConfigKey("webpPanelTerminalBlurDark",request.WebpPanelTerminalBlurDark);
        return Ok(new ApiResponse<object>
            {
                Code = 200,
                Message = "更新成功",
            }
        );
    }
}