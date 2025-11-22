using System.ComponentModel.DataAnnotations;

namespace MSLX.Daemon.Models.Instance;
public class LaunchServerRequest
{
    [Required(ErrorMessage = "服务器ID不能为空")]
    [Range(1, uint.MaxValue, ErrorMessage = "格式错误！")]
    public uint? ID { get; set; }
}