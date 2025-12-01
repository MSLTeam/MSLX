using System.ComponentModel.DataAnnotations;

namespace MSLX.Daemon.Models.Instance;

public class CreateServerRequest : IValidatableObject
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

    [RegularExpression(@"^[a-fA-F0-9]{32}$", ErrorMessage = "coreFileKey 格式不正确 (应为32位 GUID 无连字符)")]
    public string? coreFileKey { get; set; }
    
    [RegularExpression(@"^[a-fA-F0-9]{64}$", ErrorMessage = "coreSha256 必须是有效的 64 位十六进制 SHA-256 字符串")]
    public string? coreSha256 { get; set; }
    
    // 验证上传模式和下载模式只能二选一
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // 如果 coreUrl 和 coreFileKey 都有值，那就是来造反的
        if (!string.IsNullOrWhiteSpace(coreUrl) && !string.IsNullOrWhiteSpace(coreFileKey))
        {
            yield return new ValidationResult(
                "冲突：不能同时提供下载链接 (coreUrl) 和上传文件凭证 (coreFileKey)，请二选一。",
                new[] { nameof(coreUrl), nameof(coreFileKey) } 
            );
        }
        
    }
}