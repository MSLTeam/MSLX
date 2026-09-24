using System.ComponentModel.DataAnnotations;
using MSLX.SDK.Utils;

namespace MSLX.SDK.Attributes;

public class SsrfSafeUrlAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value == null || string.IsNullOrWhiteSpace(value.ToString()))
            return ValidationResult.Success; // 空值由 [Required] 负责拦截

        string urlStr = value.ToString()!;
        
        string? errorMsg = UrlSecurityGuard.CheckUrlSafety(urlStr);
        if (errorMsg != null)
        {
            return new ValidationResult(errorMsg);
        }

        return ValidationResult.Success;
    }
}
