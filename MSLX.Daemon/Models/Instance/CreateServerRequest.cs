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
    
    [RegularExpression(@"^https?://.+", ErrorMessage = "核心下载地址 (coreUrl) 必须以 http:// 或 https:// 开头")]
    public string? coreUrl { get; set; }
    
    [RegularExpression(@"^[a-fA-F0-9]{64}$", ErrorMessage = "coreSha256 必须是有效的 64 位十六进制 SHA-256 字符串")]
    public string? coreSha256 { get; set; }
}