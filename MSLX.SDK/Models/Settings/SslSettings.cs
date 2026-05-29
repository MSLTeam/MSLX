using System.ComponentModel.DataAnnotations;

namespace MSLX.SDK.Models.Settings;

public class SslSettingsResponse
{
    public bool EnableSsl { get; set; }
    public bool HasCertificate { get; set; }
    public string? CertificateContent { get; set; } 
    public bool IsSelfSigned { get; set; }
}
public class UpdateSslSettingsRequest : IValidatableObject
{
    [Required(ErrorMessage = "必须指定是否开启 SSL")]
    public bool EnableSsl { get; set; }

    // 新增：是否使用自签名证书
    public bool UseSelfSignedCert { get; set; }

    [RegularExpression(@"^-----BEGIN CERTIFICATE-----\s+[a-zA-Z0-9\+/=\r\n]+\s+-----END CERTIFICATE-----\s*$", 
        ErrorMessage = "公钥格式不正确，必须是标准的 PEM 格式文本")]
    public string? Certificate { get; set; }
    
    [RegularExpression(@"^-----BEGIN (?:RSA |EC )?PRIVATE KEY-----\s+[a-zA-Z0-9\+/=\r\n]+\s+-----END (?:RSA |EC )?PRIVATE KEY-----\s*$", 
        ErrorMessage = "私钥格式不正确，必须是标准的 PEM 私钥格式文本")]
    public string? PrivateKey { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (UseSelfSignedCert && (!string.IsNullOrWhiteSpace(Certificate) || !string.IsNullOrWhiteSpace(PrivateKey)))
        {
            yield return new ValidationResult("使用自签名证书时，不能同时提交自定义证书内容。", new[] { nameof(UseSelfSignedCert) });
        }
        
        if (!UseSelfSignedCert)
        {
            if (!string.IsNullOrWhiteSpace(Certificate) && string.IsNullOrWhiteSpace(PrivateKey))
            {
                yield return new ValidationResult("提交了公钥，必须提供对应的 PrivateKey(私钥)。", new[] { nameof(PrivateKey) });
            }
            
            if (!string.IsNullOrWhiteSpace(PrivateKey) && string.IsNullOrWhiteSpace(Certificate))
            {
                yield return new ValidationResult("提交了私钥，必须提供对应的 Certificate(公钥)。", new[] { nameof(Certificate) });
            }
        }
    }
}