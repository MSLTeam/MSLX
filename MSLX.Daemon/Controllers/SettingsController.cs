using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Models;
using MSLX.Daemon.Models.Settings;
using MSLX.Daemon.Utils.ConfigUtils;

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
                    FireWallBanLocalAddr = IConfigBase.Config.ReadConfig()["fireWallBanLocalAddr"]?? false,
                    OpenWebConsoleOnLaunch = IConfigBase.Config.ReadConfig()["openWebConsoleOnLaunch"]?? true,
                    NeoForgeInstallerMirrors = IConfigBase.Config.ReadConfig()["neoForgeInstallerMirrors"]?? "MSL Mirrors",
                    ListenHost = IConfigBase.Config.ReadConfig()["listenHost"]?? "localhost",
                    ListenPort = IConfigBase.Config.ReadConfig()["listenPort"]?? 1027,
                    OAuthMSLClientID = IConfigBase.Config.ReadConfig()["oAuthMSLClientID"]?? "",
                    OAuthMSLClientSecret = IConfigBase.Config.ReadConfig()["oAuthMSLClientSecret"]?? "",
                }
            }
        );
    }
    
    [HttpPost]
    public IActionResult UpdateSettings([FromBody] UpdateSettingsRequest request)
    {
        IConfigBase.Config.WriteConfigKey("fireWallBanLocalAddr", request.FireWallBanLocalAddr);
        IConfigBase.Config.WriteConfigKey("openWebConsoleOnLaunch", request.OpenWebConsoleOnLaunch);
        IConfigBase.Config.WriteConfigKey("neoForgeInstallerMirrors", request.NeoForgeInstallerMirrors);
        IConfigBase.Config.WriteConfigKey("listenHost", request.ListenHost);
        IConfigBase.Config.WriteConfigKey("listenPort", request.ListenPort);
        IConfigBase.Config.WriteConfigKey("oAuthMSLClientID", request.OAuthMSLClientID);
        IConfigBase.Config.WriteConfigKey("oAuthMSLClientSecret", request.OAuthMSLClientSecret);
        return Ok(new ApiResponse<object>
            {
                Code = 200,
                Message = "更新成功",
            }
        );
    }

    [HttpGet("webpanel/style")]
    [AllowAnonymous]
    public IActionResult GetWebPanelStyle()
    {
        return Ok(new ApiResponse<object>
        {
            Code = 200,
            Message = "获取成功",
            Data = new
            {
                WebPanelStyleLightBackground = IConfigBase.Config.ReadConfig()["webPanelStyleLightBackground"]?? "",
                WebPanelStyleDarkBackground = IConfigBase.Config.ReadConfig()["webPanelStyleDarkBackground"]?? "",
                WebPanelStyleLightBackgroundOpacity = IConfigBase.Config.ReadConfig()["webPanelStyleLightBackgroundOpacity"]?? 1.0,
                WebPanelStyleDarkBackgroundOpacity = IConfigBase.Config.ReadConfig()["webPanelStyleDarkBackgroundOpacity"]?? 1.0,
                WebPanelStyleLightComponentsOpacity = IConfigBase.Config.ReadConfig()["webPanelStyleLightComponentsOpacity"]?? 0.4,
                WebPanelStyleDarkComponentsOpacity = IConfigBase.Config.ReadConfig()["webPanelStyleDarkComponentsOpacity"]?? 0.6,
                WebpPanelTerminalBlurLight = IConfigBase.Config.ReadConfig()["webpPanelTerminalBlurLight"]?? 5.0,
                WebpPanelTerminalBlurDark = IConfigBase.Config.ReadConfig()["webpPanelTerminalBlurDark"]?? 5.0,

                // 日志染色级别
                WebPanelColorizeLogLevel = IConfigBase.Config.ReadConfig()["webPanelColorizeLogLevel"] ?? 1,
            }
        });
    }

    [HttpPost("webpanel/style")]
    public IActionResult UpdateWebPanelStyle([FromBody] UpdateWebPanelStyleSettingsRequest request)
    {
        IConfigBase.Config.WriteConfigKey("webPanelStyleLightBackground",request.WebPanelStyleLightBackground);
        IConfigBase.Config.WriteConfigKey("webPanelStyleDarkBackground",request.WebPanelStyleDarkBackground);
        IConfigBase.Config.WriteConfigKey("webPanelStyleLightBackgroundOpacity",request.WebPanelStyleLightBackgroundOpacity);
        IConfigBase.Config.WriteConfigKey("webPanelStyleDarkBackgroundOpacity",request.WebPanelStyleDarkBackgroundOpacity);
        IConfigBase.Config.WriteConfigKey("webPanelStyleLightComponentsOpacity",request.WebPanelStyleLightComponentsOpacity);
        IConfigBase.Config.WriteConfigKey("webPanelStyleDarkComponentsOpacity",request.WebPanelStyleDarkComponentsOpacity);
        IConfigBase.Config.WriteConfigKey("webpPanelTerminalBlurLight",request.WebpPanelTerminalBlurLight);
        IConfigBase.Config.WriteConfigKey("webpPanelTerminalBlurDark",request.WebpPanelTerminalBlurDark);
        IConfigBase.Config.WriteConfigKey("webPanelColorizeLogLevel",request.WebPanelColorizeLogLevel);
        return Ok(new ApiResponse<object>
            {
                Code = 200,
                Message = "更新成功",
            }
        );
    }
}