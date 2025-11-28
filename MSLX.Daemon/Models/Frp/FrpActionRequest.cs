using System.ComponentModel.DataAnnotations;

namespace MSLX.Daemon.Models.Frp;

public class FrpActionRequest
{
    [Required(ErrorMessage = "隧道ID (id) 不能为空")]
    public int Id { get; set; }
    
    [Required(ErrorMessage = "方法 (action)  不能为空")]
    [AllowedValues("start", "stop", ErrorMessage = "方法 (action) 错误")]
    public string Action { get; set; } 
}