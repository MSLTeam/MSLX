using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.Models;
using MSLX.SDK.Models.Settings;
using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace MSLX.Daemon.Controllers;

[ApiController]
[Route("api/settings")]
public class SettingsController : ControllerBase
{
    [HttpGet]
    [Authorize(Roles = "admin")]
    public IActionResult GetSettings()
    {
        return Ok(new ApiResponse<object>
            {
                Code = 200,
                Message = "获取成功",
                Data = new
                {
                    AllowNormalUserChangeUserName = IConfigBase.Config.ReadConfig()["allowNormalUserChangeUserName"] ?? true,
                    FireWallBanLocalAddr = IConfigBase.Config.ReadConfig()["fireWallBanLocalAddr"] ?? false,
                    OpenWebConsoleOnLaunch = IConfigBase.Config.ReadConfig()["openWebConsoleOnLaunch"] ?? true,
                    NeoForgeInstallerMirrors =
                        IConfigBase.Config.ReadConfig()["neoForgeInstallerMirrors"] ?? "MSL Mirrors",
                    ListenHost = IConfigBase.Config.ReadConfig()["listenHost"] ?? "localhost",
                    ListenPort = IConfigBase.Config.ReadConfig()["listenPort"] ?? 1027,
                    OAuthMSLClientID = IConfigBase.Config.ReadConfig()["oAuthMSLClientID"] ?? "",
                    OAuthMSLClientSecret = IConfigBase.Config.ReadConfig()["oAuthMSLClientSecret"] ?? "",
                }
            }
        );
    }

    [HttpPost]
    [Authorize(Roles = "admin")]
    public IActionResult UpdateSettings([FromBody] UpdateSettingsRequest request)
    {
        IConfigBase.Config.WriteConfigKey("allowNormalUserChangeUserName", request.AllowNormalUserChangeUserName);
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
                WebPanelStyleLightBackground = IConfigBase.Config.ReadConfig()["webPanelStyleLightBackground"] ?? "",
                WebPanelStyleDarkBackground = IConfigBase.Config.ReadConfig()["webPanelStyleDarkBackground"] ?? "",
                WebPanelStyleLightBackgroundOpacity =
                    IConfigBase.Config.ReadConfig()["webPanelStyleLightBackgroundOpacity"] ?? 1.0,
                WebPanelStyleDarkBackgroundOpacity =
                    IConfigBase.Config.ReadConfig()["webPanelStyleDarkBackgroundOpacity"] ?? 1.0,
                WebPanelStyleLightComponentsOpacity =
                    IConfigBase.Config.ReadConfig()["webPanelStyleLightComponentsOpacity"] ?? 0.4,
                WebPanelStyleDarkComponentsOpacity =
                    IConfigBase.Config.ReadConfig()["webPanelStyleDarkComponentsOpacity"] ?? 0.6,
                WebpPanelTerminalBlurLight = IConfigBase.Config.ReadConfig()["webpPanelTerminalBlurLight"] ?? 5.0,
                WebpPanelTerminalBlurDark = IConfigBase.Config.ReadConfig()["webpPanelTerminalBlurDark"] ?? 5.0,

                // 日志染色级别
                WebPanelColorizeLogLevel = IConfigBase.Config.ReadConfig()["webPanelColorizeLogLevel"] ?? 1,
            }
        });
    }

    [HttpPost("webpanel/style")]
    [Authorize(Roles = "admin")]
    public IActionResult UpdateWebPanelStyle([FromBody] UpdateWebPanelStyleSettingsRequest request)
    {
        IConfigBase.Config.WriteConfigKey("webPanelStyleLightBackground", request.WebPanelStyleLightBackground);
        IConfigBase.Config.WriteConfigKey("webPanelStyleDarkBackground", request.WebPanelStyleDarkBackground);
        IConfigBase.Config.WriteConfigKey("webPanelStyleLightBackgroundOpacity",
            request.WebPanelStyleLightBackgroundOpacity);
        IConfigBase.Config.WriteConfigKey("webPanelStyleDarkBackgroundOpacity",
            request.WebPanelStyleDarkBackgroundOpacity);
        IConfigBase.Config.WriteConfigKey("webPanelStyleLightComponentsOpacity",
            request.WebPanelStyleLightComponentsOpacity);
        IConfigBase.Config.WriteConfigKey("webPanelStyleDarkComponentsOpacity",
            request.WebPanelStyleDarkComponentsOpacity);
        IConfigBase.Config.WriteConfigKey("webpPanelTerminalBlurLight", request.WebpPanelTerminalBlurLight);
        IConfigBase.Config.WriteConfigKey("webpPanelTerminalBlurDark", request.WebpPanelTerminalBlurDark);
        IConfigBase.Config.WriteConfigKey("webPanelColorizeLogLevel", request.WebPanelColorizeLogLevel);
        return Ok(new ApiResponse<object>
            {
                Code = 200,
                Message = "更新成功",
            }
        );
    }

    #region SSL设置

    [HttpGet("ssl")]
    [Authorize(Roles = "admin")]
    public IActionResult GetSslSettings()
    {
        string certDir = Path.Combine(IConfigBase.GetAppConfigPath(), "certs");
        string pemPath = Path.Combine(certDir, "server.pem");
        string keyPath = Path.Combine(certDir, "server.key");

        bool hasCert = System.IO.File.Exists(pemPath) && System.IO.File.Exists(keyPath);
        string? certContent = hasCert ? System.IO.File.ReadAllText(pemPath) : null;
        
        bool isSelfSigned = false;
        
        var activeCert = MSLX.Daemon.Utils.SslCertificateManager.GetCertificate();
        if (activeCert != null)
        {
            isSelfSigned = activeCert.Subject.Contains("MSLX Local Certificate") || 
                           activeCert.Subject.Contains("MSLX Emergency Temporary Certificate");
        }

        return Ok(new ApiResponse<SslSettingsResponse>
        {
            Code = 200,
            Message = "获取成功",
            Data = new SslSettingsResponse
            {
                EnableSsl = (bool?)IConfigBase.Config.ReadConfig()["enableSsl"] ?? false,
                HasCertificate = hasCert,
                CertificateContent = certContent,
                IsSelfSigned = isSelfSigned
            }
        });
    }

    [HttpPost("ssl")]
    [Authorize(Roles = "admin")]
    public IActionResult UpdateSslSettings([FromBody] UpdateSslSettingsRequest request)
    {
        try
        {
            string certDir = Path.Combine(IConfigBase.GetAppConfigPath(), "certs");
            if (!Directory.Exists(certDir))
            {
                Directory.CreateDirectory(certDir);
            }

            // 自签名
            if (request.UseSelfSignedCert)
            {
                using var rsa = System.Security.Cryptography.RSA.Create(2048);
                var dnBuilder = new System.Security.Cryptography.X509Certificates.X500DistinguishedNameBuilder();
                dnBuilder.AddCommonName("MSLX Local Certificate");
                dnBuilder.AddOrganizationName("净善宫");
                dnBuilder.AddOrganizationalUnitName("纳西妲最可爱啦!");
                dnBuilder.AddLocalityName("Sumeru");
                dnBuilder.AddCountryOrRegion("CN"); 
                var req = new System.Security.Cryptography.X509Certificates.CertificateRequest(
                    dnBuilder.Build(),
                    rsa,
                    System.Security.Cryptography.HashAlgorithmName.SHA256,
                    System.Security.Cryptography.RSASignaturePadding.Pkcs1);

                // 把本地回环签进去
                var sanBuilder = new SubjectAlternativeNameBuilder();
                sanBuilder.AddDnsName("localhost");
                sanBuilder.AddIpAddress(IPAddress.Loopback);
                req.CertificateExtensions.Add(sanBuilder.Build());

                using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),
                    DateTimeOffset.UtcNow.AddYears(10));
                
                System.IO.File.WriteAllText(Path.Combine(certDir, "server.pem"), cert.ExportCertificatePem());
                System.IO.File.WriteAllText(Path.Combine(certDir, "server.key"), rsa.ExportRSAPrivateKeyPem());
            }
            // 自定义证书
            else if (!string.IsNullOrWhiteSpace(request.Certificate) && !string.IsNullOrWhiteSpace(request.PrivateKey))
            {
                System.IO.File.WriteAllText(Path.Combine(certDir, "server.pem"), request.Certificate.Trim());
                System.IO.File.WriteAllText(Path.Combine(certDir, "server.key"), request.PrivateKey.Trim());
            }
            
            if (request.EnableSsl)
            {
                string checkPem = Path.Combine(IConfigBase.GetAppConfigPath(), "certs", "server.pem");
                if (!System.IO.File.Exists(checkPem))
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Code = 400,
                        Message = "无法开启 SSL：未找到证书文件，请一起提交证书内容或勾选生成自签名证书。"
                    });
                }
            }

            IConfigBase.Config.WriteConfigKey("enableSsl", request.EnableSsl);
            if (request.EnableSsl)
            {
                SslCertificateManager.ReloadCertificate();
            }

            return Ok(new ApiResponse<object>
            {
                Code = 200,
                Message = "SSL 配置已更新，请重启面板以应用网络协议更改"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new ApiResponse<object>
            {
                Code = 500,
                Message = $"保存 SSL 配置失败: {ex.Message}"
            });
        }
    }

    #endregion
}