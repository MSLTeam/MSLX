using System.ComponentModel.DataAnnotations;

namespace MSLX.Daemon.Models.Instance;
public class CreateServerRequest
{
    [Required(ErrorMessage = "服务器名称 (name) 不能为空")]
    public string name { get; set; }
    
    [Required(ErrorMessage = "核心 (core) 不能为空")]
    public string core { get; set; }
    
    [Range(1, int.MaxValue, ErrorMessage = "最小内存 (minM) 必须大于 0")]
    public int minM { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "最大内存 (maxM) 必须大于 0")]
    public int maxM { get; set; }
    
    public string? java { get; set; } 
    public string? args { get; set; }
    
    public string? path { get; set; }
}