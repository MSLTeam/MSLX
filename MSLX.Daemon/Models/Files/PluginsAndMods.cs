using System.ComponentModel.DataAnnotations;

namespace MSLX.Daemon.Models.Files;

public class SetPluginModStateRequest
{
    [AllowedValues("plugins", "mods", ErrorMessage = "不支持的模式")]
    public string Mode { get; set; } = "plugins";
    
    [AllowedValues("enable", "disable", ErrorMessage = "不支持的操作类型")]
    public string Action { get; set; } = "disable";
    
    [Required(ErrorMessage = "目标列表 (targets) 不能为空")]
    public List<string> Targets { get; set; } = new List<string>();
}