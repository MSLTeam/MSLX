using System.ComponentModel.DataAnnotations;

namespace MSLX.Daemon.Models.Instance;

public class UpdateServerRequest : IValidatableObject
{
    [Required(ErrorMessage = "实例ID (ID) 不能为空")]
    [Range(1, int.MaxValue, ErrorMessage = "实例ID 必须大于 0")]
    public int ID { get; set; }

    [Required(ErrorMessage = "服务器名称 (Name) 不能为空")]
    public required string Name { get; set; }

    [Required(ErrorMessage = "基础路径 (Base) 不能为空")]
    public required string Base { get; set; }

    [Required(ErrorMessage = "Java环境路径 (Java) 不能为空")]
    public required string Java { get; set; }

    // 这里通常指的是当前的核心文件名，或者更新后的目标文件名
    [Required(ErrorMessage = "核心文件名 (Core) 不能为空")]
    public required string Core { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "最小内存 (MinM) 不能小于 0")]
    public int? MinM { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "最大内存 (MaxM) 不能小于 0")]
    public int? MaxM { get; set; }

    public string? Args { get; set; }

    public string YggdrasilApiAddr { get; set; } = "";

    [Range(1, 100, ErrorMessage = "备份数量限制必须在 1-100 之间")]
    public int BackupMaxCount { get; set; } = 20;

    [Range(5, int.MaxValue, ErrorMessage = "备份间隔必须大于 5")]
    public int BackupDelay { get; set; } = 10;

    public string BackupPath { get; set; } = "MSLX://Backup/Instance";

    public bool AutoRestart { get; set; } = false;
    public bool ForceAutoRestart { get; set; } = true;
    public bool RunOnStartup { get; set; } = false;
    
    // 两个编码 暂时仅支持 utf-8 和 gbk
    
    [RegularExpression(@"^(?i)(utf-8|gbk)$", ErrorMessage = "输入编码 (InputEncoding) 仅支持 'utf-8' 或 'gbk'")]
    public string InputEncoding { get; set; } = "utf-8";

    [RegularExpression(@"^(?i)(utf-8|gbk)$", ErrorMessage = "输出编码 (OutputEncoding) 仅支持 'utf-8' 或 'gbk'")]
    public string OutputEncoding { get; set; } = "utf-8";
    
    [RegularExpression(@"^(?i)(utf-8|gbk)$", ErrorMessage = "文件编码 (OutputEncoding) 仅支持 'utf-8' 或 'gbk'")]
    public string FileEncoding { get; set; } = "utf-8";
    
    // 更新的可选参数

    // 本地上传服务端 Key
    [RegularExpression(@"^[a-fA-F0-9]{32}$", ErrorMessage = "CoreFileKey 格式不正确 (32位MD5)")]
    public string? CoreFileKey { get; set; } 

    // 远程下载服务端 URL
    [RegularExpression(@"^https?://.+", ErrorMessage = "核心下载地址 (CoreUrl) 必须以 http:// 或 https:// 开头")]
    public string? CoreUrl { get; set; }

    // SHA256 校验
    [RegularExpression(@"^[a-fA-F0-9]{64}$", ErrorMessage = "CoreSha256 必须是有效的 64 位十六进制字符串")]
    public string? CoreSha256 { get; set; }

    /// <summary>
    /// 自定义逻辑验证
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // 冲突
        if (!string.IsNullOrWhiteSpace(CoreFileKey) && !string.IsNullOrWhiteSpace(CoreUrl))
        {
            yield return new ValidationResult(
                "冲突：不能同时提供 '核心文件上传 (CoreFileKey)' 和 '远程下载地址 (CoreUrl)'，请只选择一种更新方式。",
                new[] { nameof(CoreFileKey), nameof(CoreUrl) } 
            );
        }

        // 最大内存不能小于最小内存
        if (MinM.HasValue && MaxM.HasValue && MaxM < MinM)
        {
            yield return new ValidationResult(
                $"逻辑错误：最大内存 ({MaxM}) 不能小于 最小内存 ({MinM})。",
                new[] { nameof(MaxM), nameof(MinM) }
            );
        }
    }
}