using System.ComponentModel.DataAnnotations;

namespace MSLX.Daemon.Models.Settings;

public class UpdateSettingsRequest
{
    [Required(ErrorMessage = "用户名 (user) 不能为空")]
    public string User { get; set; }
    
    [Required(ErrorMessage = "头像链接 (avatar) 不能为空")]
    public string Avatar { get; set; }
    
    [Required(ErrorMessage = "防火墙配置-是否允许本地回环地址访问 (fireWallBanLocalAddr) 不能为空")]
    public Boolean FireWallBanLocalAddr { get; set; }
    
    [Required(ErrorMessage = "是否在启动时打开 Web 控制台 (openWebConsoleOnLaunch) 不能为空")]
    public Boolean OpenWebConsoleOnLaunch { get; set; }
    
}