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
    public bool ignoreEula { get; set; } = false;
    public string? path { get; set; }

    // 远程下载服务端
    [RegularExpression(@"^https?://.+", ErrorMessage = "核心下载地址 (coreUrl) 必须以 http:// 或 https:// 开头")]
    public string? coreUrl { get; set; }

    [RegularExpression(@"^[a-fA-F0-9]{64}$", ErrorMessage = "coreSha256 必须是有效的 64 位十六进制 SHA-256 字符串")]
    public string? coreSha256 { get; set; }
    
    // 本地上传服务端
    [RegularExpression(@"^[a-fA-F0-9]{32}$", ErrorMessage = "coreFileKey 格式不正确 (请先初始化上传获取此参数)")]
    public string? coreFileKey { get; set; }
    
    // 远程下载压缩包文件
    [RegularExpression(@"^https?://.+", ErrorMessage = "核心下载地址 (coreUrl) 必须以 http:// 或 https:// 开头")]
    public string? packageUrl { get; set; }

    [RegularExpression(@"^[a-fA-F0-9]{64}$", ErrorMessage = "coreSha256 必须是有效的 64 位十六进制 SHA-256 字符串")]
    public string? packageSha256 { get; set; }
    
    // 本地上传压缩包文件（可以同时传服务端jar下载）
    [RegularExpression(@"^[a-fA-F0-9]{32}$", ErrorMessage = "packageFileKey 格式不正确 (请先初始化上传获取此参数)")]
    public string? packageFileKey { get; set; }
    
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // coreFileKey是上传核心 如果还有下载参数和整合包参数 那绝对是来捣乱的 （似乎也不是不能一起？算了 以后再改叭～）
        if (!string.IsNullOrWhiteSpace(coreFileKey))
        {
            if (!string.IsNullOrWhiteSpace(packageFileKey) || !string.IsNullOrWhiteSpace(coreUrl))
            {
                yield return new ValidationResult(
                    "冲突：'单核心文件上传 (coreFileKey)' 不能与 '压缩包上传' 或 '远程下载' 同时使用。如果是整合包请使用 packageFileKey。",
                    new[] { nameof(coreFileKey) } 
                );
            }
        }

        // 你总不能又上传整合包又叫我下载吧？不可以贪心～
        if (!string.IsNullOrWhiteSpace(packageUrl) && !string.IsNullOrWhiteSpace(packageFileKey))
        {
            yield return new ValidationResult(
                "冲突：'远程下载压缩包 (packageUrl)' 不能与 '本地上传压缩包 (packageFileKey)' 同时使用。请二选一。",
                new[] { nameof(packageUrl), nameof(packageFileKey) }
            );
        }

        /*
        // 啥都不给？捣乱都不敢这么捣法吧！
        bool hasSource = !string.IsNullOrWhiteSpace(coreFileKey)
                         || !string.IsNullOrWhiteSpace(packageFileKey)
                         || !string.IsNullOrWhiteSpace(coreUrl);

        if (!hasSource && java != "none")
        {
            yield return new ValidationResult(
                "参数缺失：必须至少提供一种来源 (coreUrl, coreFileKey 或 packageFileKey)。",
                new[] { nameof(core) }
            );
        } */
    }
}

public class DeleteServerRequest
{
    [Required(ErrorMessage = "实例ID (id) 不能为空")]
    [Range(1, int.MaxValue, ErrorMessage = "实例ID (id) 必须大于 0")]
    public uint Id { get; set; }
    
    public bool? DeleteFiles { get; set; } = false;
}