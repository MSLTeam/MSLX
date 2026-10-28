using System.Security.Cryptography;
using Fido2NetLib;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using MSLX.Daemon.Services;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.Models;
using Newtonsoft.Json.Linq;

namespace MSLX.Daemon.Controllers.AuthControllers;

[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(65536)]
public sealed class AccountSecurityController(AccountSecurityService security, IMemoryCache cache) : ControllerBase
{
    private static JObject Payload<T>(T value) where T : class => JObject.FromObject(value,
        Newtonsoft.Json.JsonSerializer.Create(new Newtonsoft.Json.JsonSerializerSettings
        { ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver() }));

    private UserInfo Self()
    {
        var id = User.FindFirst("UserId")?.Value;
        if (id == null || id == "system-admin" || User.Identity?.AuthenticationType == "NodeAuth" ||
            (bool?)IConfigBase.Config.ReadConfigKey("IsSlaveMode") == true)
            throw new AccountSecurityException("请在主节点使用个人账户管理安全设置", 403);
        return IConfigBase.UserList.GetUserById(id) ?? throw new AccountSecurityException("用户不存在", 401);
    }

    private async Task<IActionResult> Run(Func<Task<object>> action)
    {
        try { return Ok(new ApiResponse<object> { Code = 200, Message = "success", Data = await action() }); }
        catch (AccountSecurityException e) { return StatusCode(e.Status, new ApiResponse<object> { Code = e.Status, Message = e.Message }); }
        catch (Exception e) when (e is Fido2VerificationException or System.Text.Json.JsonException or FormatException or ArgumentException)
        {
            return BadRequest(new ApiResponse<object> { Code = 400, Message = "通行密钥验证失败，请重新发起验证" });
        }
        catch (CryptographicException)
        {
            return StatusCode(503, new ApiResponse<object> { Code = 503, Message = "无法读取二步验证密钥，请检查 DataProtection-Keys 备份或联系管理员重置" });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return StatusCode(500, new ApiResponse<object> { Code = 500, Message = "安全设置保存失败，请检查数据目录后重试" });
        }
        catch (InvalidOperationException)
        {
            return Unauthorized(new ApiResponse<object> { Code = 401, Message = "用户不存在或状态已变更，请重新登录" });
        }
    }

    [HttpPost("api/user/security/totp/setup")]
    public Task<IActionResult> Setup([FromBody] AccountSecurityProof request) => Run(() => Task.FromResult(security.SetupTotp(Self(), request.Password)));

    [HttpPost("api/user/security/totp/enable")]
    public Task<IActionResult> Enable([FromBody] TotpEnableRequest request) => Run(() => Task.FromResult(security.EnableTotp(Self(), request.Password, request.SetupId, request.TwoFactorCode ?? "")));

    [HttpPost("api/user/security/totp/disable")]
    public Task<IActionResult> Disable([FromBody] AccountSecurityProof request) => Run(() => Task.FromResult(security.DisableTotp(Self(), request.Password, request.TwoFactorCode)));

    [HttpPost("api/user/security/passkeys/options")]
    public Task<IActionResult> RegisterOptions([FromBody] PasskeyRegistrationRequest request) => Run(() => Task.FromResult(security.RegisterOptions(Request, Self(), request.Password, request.TwoFactorCode, request.Name)));

    [HttpPost("api/user/security/passkeys/verify")]
    public Task<IActionResult> Register([FromBody] PasskeyCredentialRequest<PasskeyAttestationResponse> request) => Run(() => security.Register(Request, Self(), request.ChallengeId, Payload(request.Credential)));

    [HttpPost("api/user/security/passkeys/delete/{id}")]
    public Task<IActionResult> Delete(string id, [FromBody] AccountSecurityProof request) => Run(() => Task.FromResult(security.DeletePasskey(Self(), request.Password, request.TwoFactorCode, id)));

    [HttpPost("api/auth/passkey/options"), AllowAnonymous]
    public Task<IActionResult> LoginOptions() => Run(() => Task.FromResult(security.LoginOptions(Request)));

    [HttpPost("api/auth/passkey/verify"), AllowAnonymous]
    public Task<IActionResult> Login([FromBody] PasskeyCredentialRequest<PasskeyAssertionResponse> request) => Run(async () =>
    {
        var user = await security.LoginPasskey(Request, request.ChallengeId, Payload(request.Credential));
        var data = AccountSecurityService.CompleteLogin(user);
        cache.Remove("ERR_COUNT_" + HttpContext.Connection.RemoteIpAddress);
        cache.Remove("ACCOUNT_" + user.Id);
        return data;
    });
}
