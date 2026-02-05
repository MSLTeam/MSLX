using System.ComponentModel.DataAnnotations;

namespace MSLX.Daemon.Models.Instance;

public class DeleteBackupRequest : IValidatableObject
{
    [Range(1, uint.MaxValue, ErrorMessage = "实例ID必须有效")]
    public uint Id { get; set; }

    [Required(ErrorMessage = "文件名不能为空")]
    public string FileName { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // 拦截路径遍历攻击
        if (FileName.Contains("/") || FileName.Contains("\\") || FileName.Contains(".."))
        {
            yield return new ValidationResult(
                "文件名包含非法字符，禁止路径跳转操作。",
                new[] { nameof(FileName) }
            );
        }

        // 拦截非法字符
        if (FileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            yield return new ValidationResult(
                "文件名包含系统不允许的特殊字符。",
                new[] { nameof(FileName) }
            );
        }

        // 拦截非 zip 文件
        if (!FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            yield return new ValidationResult(
               "仅允许删除 .zip 格式的备份文件。",
               new[] { nameof(FileName) }
           );
        }
    }
}
