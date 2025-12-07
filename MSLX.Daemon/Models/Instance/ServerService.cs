using System.ComponentModel.DataAnnotations;

namespace MSLX.Daemon.Models.Instance;
public class ServerActionRequest
{
    [Required(ErrorMessage = "服务器ID不能为空")]
    [Range(1, uint.MaxValue, ErrorMessage = "格式错误！")]
    public uint? ID { get; set; }

    [Required(ErrorMessage = "操作不能为空")]
    [AllowedValues("start", "stop", ErrorMessage = "操作 (action) 错误")]
    public string Action { get; set; }
}