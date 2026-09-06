using System.ComponentModel.DataAnnotations;

namespace MSLX.SDK.Models.Tasks;

public class CancelCreationRequest
{
    [Required(ErrorMessage = "服务器ID (serverId) 不能为空")]
    public string ServerId { get; set; }
    
    /// <summary>
    /// 是否清理已部署的文件（删除实例文件夹）
    /// </summary>
    public bool CleanupFiles { get; set; } = false;
}
