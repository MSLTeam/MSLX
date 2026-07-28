using System.ComponentModel.DataAnnotations;

namespace MSLX.SDK.Models.Tasks;

public class CancelCreationRequest
{
    [Required(ErrorMessage = "服务器ID (serverId) 不能为空")]
    public string ServerId { get; set; }
}
