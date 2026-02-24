using System.ComponentModel.DataAnnotations;

namespace MSLX.Daemon.Models.Frp;

public class CreateFrpRequest
{
    [Required(ErrorMessage = "隧道名称 (name) 不能为空")]
    public string name { get; set; }
    
    [Required(ErrorMessage = "配置文件 (config) 不能为空")]
    public string config { get; set; }
    
    // 在这里限制传入的服务提供商
    [Required(ErrorMessage = "提供者 (provider) 不能为空")]
    [AllowedValues("MSLFrp", "Custom","MSL P2P","ME Frp", ErrorMessage = "提供者 (provider) 错误")]
    public string provider { get; set; }
    
    [Required(ErrorMessage = "配置文件格式 (format) 不能为空")]
    [AllowedValues("toml", "ini","cmd", ErrorMessage = "配置文件格式 (format) 错误")]
    public string format { get; set; }
}

public class DeleteFrpRequest
{
    [Required(ErrorMessage = "隧道ID (id) 不能为空")]
    public int id { get; set; }
}