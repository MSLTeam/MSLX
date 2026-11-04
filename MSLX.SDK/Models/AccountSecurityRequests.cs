using System.ComponentModel.DataAnnotations;
using Newtonsoft.Json.Linq;

namespace MSLX.SDK.Models;

public class AccountSecurityProof
{
    [Required(ErrorMessage = "请输入当前密码"), StringLength(1024, ErrorMessage = "密码最长为 1024 个字符")]
    public string Password { get; set; } = "";

    [StringLength(64, ErrorMessage = "验证码最长为 64 个字符")]
    public virtual string? TwoFactorCode { get; set; }
}

public sealed class TotpEnableRequest : AccountSecurityProof
{
    [Required(ErrorMessage = "请输入验证器动态码"), StringLength(64, ErrorMessage = "验证码最长为 64 个字符")]
    public override string? TwoFactorCode { get; set; }
    [Required(ErrorMessage = "请先生成绑定二维码"), StringLength(128)]
    public string SetupId { get; set; } = "";
}

public sealed class PasskeyRegistrationRequest : AccountSecurityProof
{
    [Required(ErrorMessage = "请输入通行密钥名称"), StringLength(64, ErrorMessage = "名称最长为 64 个字符")]
    public string Name { get; set; } = "";
}

public sealed class PasskeyCredentialRequest<TResponse>
{
    [Required(ErrorMessage = "缺少通行密钥挑战标识"), StringLength(128)]
    public string ChallengeId { get; set; } = "";

    [Required(ErrorMessage = "缺少通行密钥响应")]
    public PasskeyCredentialPayload<TResponse> Credential { get; set; } = null!;
}

public sealed class PasskeyCredentialPayload<TResponse>
{
    [Required, StringLength(2048), RegularExpression(@"[A-Za-z0-9_-]+")]
    public string Id { get; set; } = "";

    [Required, StringLength(2048), RegularExpression(@"[A-Za-z0-9_-]+")]
    public string RawId { get; set; } = "";

    [Required, RegularExpression("public-key")]
    public string Type { get; set; } = "";

    [StringLength(32)]
    public string? AuthenticatorAttachment { get; set; }
    public JObject? ClientExtensionResults { get; set; }
    [Required]
    public TResponse Response { get; set; } = default!;
}

public class PasskeyClientDataResponse
{
    [Required, StringLength(16384), RegularExpression(@"[A-Za-z0-9_-]+")]
    public string ClientDataJSON { get; set; } = "";
}

public sealed class PasskeyAttestationResponse : PasskeyClientDataResponse
{
    [Required, StringLength(49152), RegularExpression(@"[A-Za-z0-9_-]+")]
    public string AttestationObject { get; set; } = "";

    [MaxLength(8)]
    public string[] Transports { get; set; } = [];
}

public sealed class PasskeyAssertionResponse : PasskeyClientDataResponse
{
    [Required, StringLength(8192), RegularExpression(@"[A-Za-z0-9_-]+")]
    public string AuthenticatorData { get; set; } = "";

    [Required, StringLength(8192), RegularExpression(@"[A-Za-z0-9_-]+")]
    public string Signature { get; set; } = "";

    [Required, StringLength(1024), RegularExpression(@"[A-Za-z0-9_-]+")]
    public string UserHandle { get; set; } = "";
}
