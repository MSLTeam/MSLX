using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.Models;
using MSLX.SDK.Models.Settings;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MSLX.Daemon.Services.NodeServices;
using MSLX.Daemon.Services.PluginsService;
using MSLX.SDK.Models.Node;

namespace MSLX.Daemon.Controllers;

[ApiController]
[Route("api/settings")]
public class SettingsController : ControllerBase
{
    [HttpPost("sync")]
    [Authorize(Roles = "admin")]
    public IActionResult SyncSettings([FromBody] SettingsSyncRequest request)
    {
        try
        {
            NodeSettingsSync.Validate(request.Values);
            IConfigBase.Config.UpdateConfig(current =>
            {
                var selected = NodeSettingsSync.Select(current, request.Values.Properties().Select(p => p.Name));
                if (string.IsNullOrEmpty(request.ExpectedRevision) || NodeSettingsSync.Revision(selected) != request.ExpectedRevision)
                    throw new SyncConflictException("目标设置已变化，请重新预览");
                foreach (var property in request.Values.Properties()) current[property.Name] = property.Value.DeepClone();
                return current;
            });
            return Ok(new ApiResponse<object> { Code = 200, Message = "所选设置已同步" });
        }
        catch (SyncConflictException ex) { return Conflict(new ApiResponse<object> { Code = 409, Message = ex.Message }); }
        catch (InvalidDataException ex) { return BadRequest(new ApiResponse<object> { Code = 400, Message = ex.Message }); }
    }

    [HttpGet]
    [Authorize(Roles = "admin")]
    public IActionResult GetSettings()
    {
        bool isEmbeddedDaemon = IsEmbeddedDaemon();
        var config = IConfigBase.Config.ReadConfig();
        bool canManageMode = User?.IsInRole("admin") == true && User.Identity?.AuthenticationType != "NodeAuth";
        if (HttpContext != null) Response.Headers.CacheControl = "no-store";

        return Ok(new ApiResponse<object>
            {
                Code = 200,
                Message = "获取成功",
                Data = new
                {
                    AllowNormalUserChangeUserName = IConfigBase.Config.ReadConfig()["allowNormalUserChangeUserName"] ?? true,
                    AllowNormalUserEditFrpConfig = IConfigBase.Config.ReadConfig()["allowNormalUserEditFrpConfig"] ?? true,
                    AllowNormalUserOfflineDownload = IConfigBase.Config.ReadConfig()["allowNormalUserOfflineDownload"] ?? false,
                    EnableSsrfProtection = IConfigBase.Config.ReadConfig()["enableSsrfProtection"] ?? true,
                    FireWallBanLocalAddr = isEmbeddedDaemon ? false : config["fireWallBanLocalAddr"] ?? false,
                    OpenWebConsoleOnLaunch = config["openWebConsoleOnLaunch"] ?? true,
                    NeoForgeInstallerMirrors =
                        IConfigBase.Config.ReadConfig()["neoForgeInstallerMirrors"] ?? "MSL Mirrors",
                    ListenHost = isEmbeddedDaemon ? "localhost" : config["listenHost"] ?? "localhost",
                    ListenPort = config["listenPort"] ?? 1027,
                    AllowExternalAccess = isEmbeddedDaemon ? config["allowExternalAccess"] ?? false : false,
                    IsEmbeddedDaemon = isEmbeddedDaemon,
                    IsSlaveMode = (bool?)config["IsSlaveMode"] ?? false,
                    CanManageNodeMode = canManageMode,
                    ModeLocked = NodeModeService.ModeLocked,
                    LinkKeyLocked = NodeModeService.LinkKeyLocked,
                    SlaveLinkKey = canManageMode ? config["SlaveLinkKey"]?.ToString() : null,
                    OAuthMSLClientID = config["oAuthMSLClientID"] ?? "",
                    OAuthMSLClientSecret = config["oAuthMSLClientSecret"] ?? "",
                    DownloadThreadCount = config["downloadThreadCount"] ?? 5,
                    EnableCdnProxy = config["enableCdnProxy"] ?? false,
                    CdnProxyIpHeader = config["cdnProxyIpHeader"] ?? "X-Forwarded-For",
                    CdnProxySecretValue = config["cdnProxySecretValue"] ?? "",
                }
            }
        );
    }

    [HttpPost]
    [Authorize(Roles = "admin")]
    public IActionResult UpdateSettings([FromBody] UpdateSettingsRequest request)
    {
        bool isEmbeddedDaemon = IsEmbeddedDaemon();

        if (User?.Identity?.AuthenticationType == "NodeAuth" &&
            (request.IsSlaveMode.HasValue || request.SlaveLinkKey != null))
            return StatusCode(403, new ApiResponse<object> { Code = 403, Message = "运行模式和配对密钥仅允许本机管理员修改" });

        try
        {
            NodeModeService.UpdateSettings(request.IsSlaveMode, request.SlaveLinkKey, config =>
            {
                config["allowNormalUserChangeUserName"] = request.AllowNormalUserChangeUserName;
                config["allowNormalUserEditFrpConfig"] = request.AllowNormalUserEditFrpConfig;
                config["allowNormalUserOfflineDownload"] = request.AllowNormalUserOfflineDownload;
                config["enableSsrfProtection"] = request.EnableSsrfProtection;
                if (!isEmbeddedDaemon)
                {
                    config["fireWallBanLocalAddr"] = request.FireWallBanLocalAddr;
                }
                config["openWebConsoleOnLaunch"] = request.OpenWebConsoleOnLaunch;
                config["neoForgeInstallerMirrors"] = request.NeoForgeInstallerMirrors;
                if (!isEmbeddedDaemon)
                {
                    config["listenHost"] = request.ListenHost;
                }
                config["listenPort"] = request.ListenPort;
                if (isEmbeddedDaemon)
                {
                    config["allowExternalAccess"] = request.AllowExternalAccess;
                }
                config["oAuthMSLClientID"] = request.OAuthMSLClientID;
                config["oAuthMSLClientSecret"] = request.OAuthMSLClientSecret;
                config["downloadThreadCount"] = request.DownloadThreadCount;
                config["enableCdnProxy"] = request.EnableCdnProxy;
                config["cdnProxyIpHeader"] = request.CdnProxyIpHeader ?? "X-Forwarded-For";
                config["cdnProxySecretValue"] = request.CdnProxySecretValue ?? "";
            });
        }
        catch (SyncConflictException ex)
        { return Conflict(new ApiResponse<object> { Code = 409, Message = ex.Message }); }
        catch (InvalidDataException ex)
        { return BadRequest(new ApiResponse<object> { Code = 400, Message = ex.Message }); }

        return Ok(new ApiResponse<object>
            {
                Code = 200,
                Message = "更新成功",
            }
        );
    }

    /// <summary>
    /// 判断当前 Daemon 是否由 macOS Desktop 应用内置管理。
    /// </summary>
    private static bool IsEmbeddedDaemon()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("MSLX_EMBEDDED_DAEMON"),
            "true",
            StringComparison.OrdinalIgnoreCase);
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

                // 默认主题样式
                WebPanelDefaultThemeMode = IConfigBase.Config.ReadConfig()["webPanelDefaultThemeMode"] ?? "auto",
                WebPanelDefaultBrandTheme = IConfigBase.Config.ReadConfig()["webPanelDefaultBrandTheme"] ?? "default",
                WebPanelDefaultLayout = IConfigBase.Config.ReadConfig()["webPanelDefaultLayout"] ?? "side",
                WebPanelDefaultEnableCustomTheme = IConfigBase.Config.ReadConfig()["webPanelDefaultEnableCustomTheme"] ?? false,
                WebPanelDefaultExpandMutex = IConfigBase.Config.ReadConfig()["webPanelDefaultExpandMutex"] ?? true,
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

        // 默认主题样式
        IConfigBase.Config.WriteConfigKey("webPanelDefaultThemeMode", request.WebPanelDefaultThemeMode);
        IConfigBase.Config.WriteConfigKey("webPanelDefaultBrandTheme", request.WebPanelDefaultBrandTheme);
        IConfigBase.Config.WriteConfigKey("webPanelDefaultLayout", request.WebPanelDefaultLayout);
        IConfigBase.Config.WriteConfigKey("webPanelDefaultEnableCustomTheme", request.WebPanelDefaultEnableCustomTheme);
        IConfigBase.Config.WriteConfigKey("webPanelDefaultExpandMutex", request.WebPanelDefaultExpandMutex);
        return Ok(new ApiResponse<object>
            {
                Code = 200,
                Message = "更新成功",
            }
        );
    }

    #region SSL设置

    private static readonly object SslSettingsLock = new();

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
        
        var subject = SslCertificateManager.CurrentSubject;
        if (subject == null && certContent != null)
        {
            try { using var cert = X509Certificate2.CreateFromPem(certContent); subject = cert.Subject; }
            catch (System.Security.Cryptography.CryptographicException) { /* Invalid saved certificates remain editable. */ }
        }
        isSelfSigned = subject?.Contains("MSLX Local Certificate") == true || subject?.Contains("MSLX Emergency Temporary Certificate") == true;

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
        lock (SslSettingsLock)
        {
            return SaveSslSettings(request);
        }
    }

    private IActionResult SaveSslSettings(UpdateSslSettingsRequest request)
    {
        X509Certificate2? loadedCertificate = null;
        try
        {
            string certDir = Path.Combine(IConfigBase.GetAppConfigPath(), "certs");
            string pemPath = Path.Combine(certDir, "server.pem");
            string keyPath = Path.Combine(certDir, "server.key");
            string? certificatePem = request.Certificate?.Trim();
            string? privateKeyPem = request.PrivateKey?.Trim();

            if (request.UseSelfSignedCert)
            {
                using var rsa = RSA.Create(2048);
                var dnBuilder = new X500DistinguishedNameBuilder();
                dnBuilder.AddCommonName("MSLX Local Certificate");
                dnBuilder.AddOrganizationName("净善宫");
                dnBuilder.AddOrganizationalUnitName("纳西妲最可爱啦!");
                dnBuilder.AddLocalityName("Sumeru");
                dnBuilder.AddCountryOrRegion("CN");
                var req = new CertificateRequest(dnBuilder.Build(), rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

                var sanBuilder = new SubjectAlternativeNameBuilder();
                sanBuilder.AddDnsName("localhost");
                sanBuilder.AddIpAddress(IPAddress.Loopback);
                req.CertificateExtensions.Add(sanBuilder.Build());

                // PEM 分别保存证书和私钥，签发时无需把私钥附加到证书。
                var now = DateTimeOffset.UtcNow;
                using var cert = req.Create(req.SubjectName, X509SignatureGenerator.CreateForRSA(rsa, RSASignaturePadding.Pkcs1),
                    now.AddDays(-1), now.AddYears(10), RandomNumberGenerator.GetBytes(16));
                certificatePem = cert.ExportCertificatePem();
                privateKeyPem = rsa.ExportRSAPrivateKeyPem();
            }

            bool replaceCertificate = !string.IsNullOrEmpty(certificatePem) && !string.IsNullOrEmpty(privateKeyPem);
            if (request.EnableSsl && !replaceCertificate &&
                (!System.IO.File.Exists(pemPath) || !System.IO.File.Exists(keyPath)))
            {
                return BadRequest(new ApiResponse<object>
                {
                    Code = 400,
                    Message = "无法开启 SSL：请提交完整的证书和私钥，或生成自签名证书。"
                });
            }

            // 写入前确认 HTTPS 证书可加载，避免错误的私钥覆盖已有配置。
            if (request.EnableSsl || (replaceCertificate && !request.UseSelfSignedCert))
            {
                try
                {
                    loadedCertificate = SslCertificateManager.LoadCertificate(
                        replaceCertificate ? certificatePem! : System.IO.File.ReadAllText(pemPath),
                        replaceCertificate ? privateKeyPem! : System.IO.File.ReadAllText(keyPath));
                }
                catch (CryptographicException ex)
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Code = 400,
                        Message = $"无法加载 SSL 证书和私钥，请检查证书与私钥是否匹配。{ex.Message}"
                    });
                }
            }

            if (replaceCertificate)
            {
                Directory.CreateDirectory(certDir);
                System.IO.File.WriteAllText(pemPath, certificatePem!);
                System.IO.File.WriteAllText(keyPath, privateKeyPem!);
            }

            IConfigBase.Config.UpdateConfig(config =>
            {
                config["enableSsl"] = request.EnableSsl;
                return config;
            });
            if (request.EnableSsl)
            {
                SslCertificateManager.ReplaceCertificate(loadedCertificate!);
                loadedCertificate = null;
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
        finally
        {
            loadedCertificate?.Dispose();
        }
    }

    #endregion
}
