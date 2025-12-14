using System.ComponentModel.DataAnnotations;

namespace MSLX.Daemon.Models.Settings;

public class UpdateSettingsRequest
{
    [Required(ErrorMessage = "防火墙配置-是否允许本地回环地址访问 (fireWallBanLocalAddr) 不能为空")]
    public Boolean FireWallBanLocalAddr { get; set; }
    
    [Required(ErrorMessage = "是否在启动时打开 Web 控制台 (openWebConsoleOnLaunch) 不能为空")]
    public Boolean OpenWebConsoleOnLaunch { get; set; }
    
    [AllowedValues("Official", "MSL Mirrors","MSL Mirrors Backup", ErrorMessage = "NeoForge/Forge镜像源参数 (neoForgeInstallerMirrors) 错误")]
    [Required(ErrorMessage = "NeoForge/Forge安装镜像源 (neoForgeInstallerMirrors) 不能为空")]
    public string NeoForgeInstallerMirrors { get; set; }
    
    [Required(ErrorMessage = "监听地址 (listenHost) 不能为空")]
    public string ListenHost { get; set; }
    
    [Required(ErrorMessage = "监听端口 (listenPort) 不能为空")]
    [Range(1, 65536, ErrorMessage = "监听端口 (listenPort) 错误")]
    public uint ListenPort { get; set; }
    
}