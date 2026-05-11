using System.ComponentModel.DataAnnotations;

namespace MSLX.SDK.Models.Plugins;

public class PluginActionRequest
{
    [Required(ErrorMessage = "插件 ID 不能为空")]
    [MinLength(2, ErrorMessage = "插件 ID 长度不合法")]
    public required string Id { get; init; }
    
    [Required(ErrorMessage = "操作类型不能为空")]
    [AllowedValues("enable", "disable", "delete", "cancel", ErrorMessage = "未知的操作类型")]
    public required string Action { get; init; } 
}