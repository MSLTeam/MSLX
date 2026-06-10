using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

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

    public bool UseSelfSignedCert { get; set; }

    public string? Certificate { get; set; }

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
                yield return new ValidationResult("提交了证书，必须提供对应的私钥。", new[] { nameof(PrivateKey) });

            if (string.IsNullOrWhiteSpace(PrivateKey) && !string.IsNullOrWhiteSpace(Certificate))
                yield return new ValidationResult("提交了私钥，必须提供对应的证书。", new[] { nameof(Certificate) });
        }

        // 验证证书格式
        if (!string.IsNullOrWhiteSpace(Certificate) && !CertificateHelper.IsValidCertificate(Certificate, out var certErr))
        {
            yield return new ValidationResult(certErr, new[] { nameof(Certificate) });
        }

        if (!string.IsNullOrWhiteSpace(PrivateKey) && !CertificateHelper.IsValidPrivateKey(PrivateKey, out var keyErr))
        {
            yield return new ValidationResult(keyErr, new[] { nameof(PrivateKey) });
        }
    }
}

public static class CertificateHelper
{
    public static bool IsValidCertificate(string pem, out string? errorMessage)
    {
        try
        {
            using var cert = X509Certificate2.CreateFromPem(pem);
            errorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"证书格式无效: {ex.Message}";
            return false;
        }
    }

    public static bool IsValidPrivateKey(string pem, out string? errorMessage)
    {
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(pem);
            errorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"私钥格式无效: {ex.Message}";
            return false;
        }
    }
}