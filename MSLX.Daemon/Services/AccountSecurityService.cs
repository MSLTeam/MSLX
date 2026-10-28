using System.Net;
using System.Security.Cryptography;
using System.Text;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.Models;
using Newtonsoft.Json.Linq;
using OtpNet;

namespace MSLX.Daemon.Services;

public sealed class AccountSecurityException(string message, int status = 400) : Exception(message)
{
    public int Status { get; } = status;
}

public sealed class AccountSecurityService(IMemoryCache cache, IDataProtectionProvider protection, TimeProvider? clock = null)
{
    private readonly object _gate = new();
    private readonly IDataProtector _protector = protection.CreateProtector("MSLX.Account.Totp.v1");
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private sealed record Pending(string Purpose, string Origin, string? UserId, int Version, string Payload, string Name = "", DateTimeOffset ExpiresAt = default);
    private sealed record Failures(int Count, DateTimeOffset Until);
    private DateTimeOffset Now => _clock.GetUtcNow();

    public static bool PasswordMatches(UserInfo user, string password)
    {
        try { return !string.IsNullOrEmpty(user.PasswordHash) && BCrypt.Net.BCrypt.Verify(password, user.PasswordHash); }
        catch (BCrypt.Net.SaltParseException) { return false; }
    }

    public static bool HasTotp(UserInfo? user) => !string.IsNullOrEmpty(user?.Authentication?.ProtectedTotpSecret);
    public static AccountSecuritySummary Summary(UserInfo user) => new()
    {
        TotpEnabled = HasTotp(user),
        RecoveryCodesRemaining = user.Authentication?.RecoveryCodeHashes.Count ?? 0,
        Passkeys = user.Authentication?.Passkeys.Select(p => new PasskeySummary(UrlId(p.Id), p.Name, p.RpId, p.CreatedAt, p.LastUsedAt)).ToList() ?? []
    };

    public static object LoginData(UserInfo user) => new
    {
        token = JwtUtils.GenerateToken(user),
        userInfo = new { user.Id, user.Username, user.Avatar, user.Role, user.Resources, user.LastLoginTime }
    };

    public static object CompleteLogin(UserInfo authenticated)
    {
        var user = IConfigBase.UserList.MutateAuthentication(authenticated.Id, current =>
        {
            EnsureVersion(current, authenticated.TokenVersion);
            current.LastLoginTime = DateTime.Now;
        });
        var path = Path.Combine(IConfigBase.GetAppDataPath(), "默认账户信息.txt");
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        return LoginData(user);
    }

    public UserInfo VerifyLogin(UserInfo user, string code)
    {
        lock (_gate)
        {
            return IConfigBase.UserList.MutateAuthentication(user.Id, current =>
            {
                EnsureVersion(current, user.TokenVersion);
                if (current.PasswordHash != user.PasswordHash) throw new AccountSecurityException("登录信息已变更，请重新登录", 401);
                VerifyCode(current, code);
            });
        }
    }

    public object SetupTotp(UserInfo user, string password)
    {
        var current = Management(user, password, null, u =>
        {
            if (HasTotp(u)) throw new AccountSecurityException("请先关闭现有二步验证再重新绑定");
        });
        var secret = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));
        var id = Store(new("totp", "", current.Id, current.TokenVersion, secret));
        var uri = $"otpauth://totp/{Uri.EscapeDataString("MSLX:" + current.Username)}?secret={secret}&issuer=MSLX&algorithm=SHA1&digits=6&period=30";
        return new { setupId = id, secret, uri };
    }

    public object EnableTotp(UserInfo user, string password, string setupId, string code)
    {
        var pending = Take(setupId, "totp", "", user.Id, false);
        var recoveryCodes = Enumerable.Range(0, 10).Select(_ => Convert.ToHexString(RandomNumberGenerator.GetBytes(16))).ToArray();
        var current = Management(user, password, null, u =>
        {
            EnsureVersion(u, pending.Version);
            if (HasTotp(u)) throw new AccountSecurityException("二步验证已经启用");
            u.Authentication ??= new();
            u.Authentication.ProtectedTotpSecret = _protector.Protect(pending.Payload);
            VerifyCode(u, code);
            u.Authentication.RecoveryCodeHashes = recoveryCodes.Select(HashCode).ToList();
            u.TokenVersion++;
        });
        cache.Remove("CEREMONY_" + setupId);
        return new { token = JwtUtils.GenerateToken(current), recoveryCodes };
    }

    public object DisableTotp(UserInfo user, string password, string? code)
    {
        var current = Management(user, password, code, u =>
        {
            if (!HasTotp(u)) throw new AccountSecurityException("尚未启用二步验证");
            ClearTotp(u);
            u.TokenVersion++;
        });
        return new { token = JwtUtils.GenerateToken(current) };
    }

    public static void ClearTotp(UserInfo user)
    {
        if (user.Authentication == null) return;
        user.Authentication.ProtectedTotpSecret = null;
        user.Authentication.LastTotpStep = -1;
        user.Authentication.RecoveryCodeHashes.Clear();
    }

    private UserInfo Management(UserInfo expected, string password, string? code, Action<UserInfo> action)
    {
        if (expected.Id == "system-admin") throw new AccountSecurityException("系统 APIKey 账户不能绑定个人认证信息", 403);
        lock (_gate)
        {
            CheckFailures("ACCOUNT_" + expected.Id);
            return IConfigBase.UserList.MutateAuthentication(expected.Id, user =>
            {
                EnsureVersion(user, expected.TokenVersion);
                if (string.IsNullOrEmpty(password) || !PasswordMatches(user, password))
                {
                    Fail("ACCOUNT_" + user.Id);
                    throw new AccountSecurityException("当前密码不正确", 400);
                }
                if (HasTotp(user)) VerifyCode(user, code ?? "");
                action(user);
            });
        }
    }

    private void VerifyCode(UserInfo user, string code)
    {
        var key = "ACCOUNT_" + user.Id;
        CheckFailures(key);
        var auth = user.Authentication;
        bool valid = false;
        if (!string.IsNullOrEmpty(auth?.ProtectedTotpSecret) && code.Length <= 64)
        {
            var normalized = code.Trim().Replace("-", "").Replace(" ", "").ToUpperInvariant();
            var hash = HashCode(normalized);
            var index = auth.RecoveryCodeHashes.FindIndex(h => CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(h), Encoding.ASCII.GetBytes(hash)));
            if (index >= 0)
            {
                auth.RecoveryCodeHashes.RemoveAt(index);
                valid = true;
            }
            else if (normalized.Length == 6 && normalized.All(char.IsAsciiDigit))
            {
                var secret = _protector.Unprotect(auth.ProtectedTotpSecret);
                if (new Totp(Base32Encoding.ToBytes(secret)).VerifyTotp(Now.UtcDateTime, normalized, out var step, new VerificationWindow(1, 1)) && step > auth.LastTotpStep)
                {
                    auth.LastTotpStep = step;
                    valid = true;
                }
            }
        }
        if (!valid)
        {
            Fail(key);
            throw new AccountSecurityException("验证码无效或已使用，请使用新的动态码或恢复码", 400);
        }
        // Failed attempts expire naturally. Never clear them before the entire operation is durably saved.
    }

    private static string HashCode(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static void EnsureVersion(UserInfo user, int expected)
    {
        if (user.TokenVersion != expected) throw new AccountSecurityException("账户安全设置已变更，请重新登录", 401);
    }

    public static (string Origin, string RpId) Site(HttpRequest request)
    {
        var host = request.Host.Host;
        if (string.IsNullOrEmpty(host) || IPAddress.TryParse(host, out _))
            throw new AccountSecurityException("通行密钥需要同源 HTTPS 域名或 http://localhost");
        // Public WebAuthn sites always use HTTPS. The transport from a CDN/proxy may be HTTP;
        // derive the authority from the server-observed Host, never from the client-supplied Origin.
        var scheme = string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ? request.Scheme : "https";
        var origin = new Uri($"{scheme}://{request.Host.Value}").GetLeftPart(UriPartial.Authority);
        var suppliedOrigin = request.Headers.Origin.ToString();
        if (suppliedOrigin.Length > 0 && !string.Equals(suppliedOrigin, origin, StringComparison.OrdinalIgnoreCase))
            throw new AccountSecurityException("通行密钥仅支持同源面板", 403);
        return (origin, host.ToLowerInvariant());
    }

    // Expose only site-wide availability, never individual account/credential details.
    public static bool HasPasskeysForSite(HttpRequest request)
    {
        try
        {
            var (_, rpId) = Site(request);
            return IConfigBase.UserList.GetAllUsers().Any(u => u?.Id != "system-admin" &&
                u?.Authentication?.Passkeys.Any(p => p.RpId == rpId) == true);
        }
        catch (AccountSecurityException) { return false; }
    }

    private static Fido2 Fido(string origin, string rpId) => new(new Fido2Configuration
    {
        RPID = rpId, RPName = "MSLX", Origins = new HashSet<string> { origin }, Timeout = 300000
    });

    public object LoginOptions(HttpRequest request)
    {
        CheckIp(request.HttpContext);
        LimitOptions(request.HttpContext);
        var (origin, rpId) = Site(request);
        var options = Fido(origin, rpId).GetAssertionOptions(new GetAssertionOptionsParams
        {
            UserVerification = UserVerificationRequirement.Required
        });
        return new { challengeId = Store(new("login", origin, null, 0, options.ToJson())), options = JObject.Parse(options.ToJson()) };
    }

    public object RegisterOptions(HttpRequest request, UserInfo user, string password, string? code, string name)
    {
        var (origin, rpId) = Site(request);
        if (string.IsNullOrWhiteSpace(name) || name.Length > 64) throw new AccountSecurityException("请输入 1-64 字符的通行密钥名称");
        LimitOptions(request.HttpContext);
        var current = Management(user, password, code, u =>
        {
            if ((u.Authentication?.Passkeys.Count ?? 0) >= 20) throw new AccountSecurityException("最多绑定 20 个通行密钥");
        });
        var options = Fido(origin, rpId).RequestNewCredential(new RequestNewCredentialParams
        {
            User = new Fido2User { Id = Encoding.UTF8.GetBytes(current.Id), Name = current.Username, DisplayName = string.IsNullOrWhiteSpace(current.Name) ? current.Username : current.Name },
            ExcludeCredentials = current.Authentication?.Passkeys.Where(p => p.RpId == rpId).Select(p => new PublicKeyCredentialDescriptor(Convert.FromBase64String(p.Id))).ToArray() ?? [],
            AuthenticatorSelection = new AuthenticatorSelection { ResidentKey = ResidentKeyRequirement.Required, UserVerification = UserVerificationRequirement.Required },
            AttestationPreference = AttestationConveyancePreference.None
        });
        return new { challengeId = Store(new("register", origin, current.Id, current.TokenVersion, options.ToJson(), name.Trim())), options = JObject.Parse(options.ToJson()) };
    }

    public async Task<object> Register(HttpRequest request, UserInfo user, string challengeId, JObject credential)
    {
        var (origin, rpId) = Site(request);
        var pending = Take(challengeId, "register", origin, user.Id);
        var response = System.Text.Json.JsonSerializer.Deserialize<AuthenticatorAttestationRawResponse>(credential.ToString())
            ?? throw new AccountSecurityException("无效的通行密钥响应");
        var result = await Fido(origin, rpId).MakeNewCredentialAsync(new MakeNewCredentialParams
        {
            AttestationResponse = response,
            OriginalOptions = CredentialCreateOptions.FromJson(pending.Payload),
            IsCredentialIdUniqueToUserCallback = (args, _) => Task.FromResult(!IConfigBase.UserList.GetAllUsers().Any(u => u?.Authentication?.Passkeys.Any(p => p.Id == Convert.ToBase64String(args.CredentialId)) == true))
        });
        UserInfo current;
        lock (_gate)
        {
            var id = Convert.ToBase64String(result.Id);
            if (IConfigBase.UserList.GetAllUsers().Any(u => u?.Authentication?.Passkeys.Any(p => p.Id == id) == true))
                throw new AccountSecurityException("该通行密钥已经绑定");
            current = IConfigBase.UserList.MutateAuthentication(user.Id, u =>
            {
                EnsureVersion(u, pending.Version);
                u.Authentication ??= new();
                if (u.Authentication.Passkeys.Count >= 20) throw new AccountSecurityException("最多绑定 20 个通行密钥");
                u.Authentication.Passkeys.Add(new AccountPasskey
                {
                    Id = id, PublicKey = Convert.ToBase64String(result.PublicKey), RpId = rpId, Name = pending.Name,
                    SignCount = result.SignCount, IsBackupEligible = result.IsBackupEligible, IsBackedUp = result.IsBackedUp,
                    Transports = result.Transports.Select(t => t.ToString()).ToArray(), CreatedAt = Now.UtcDateTime
                });
                u.TokenVersion++;
            });
        }
        return new { token = JwtUtils.GenerateToken(current) };
    }

    public async Task<UserInfo> LoginPasskey(HttpRequest request, string challengeId, JObject credential)
    {
        CheckIp(request.HttpContext);
        try
        {
            var (origin, rpId) = Site(request);
            var pending = Take(challengeId, "login", origin, null);
            var response = System.Text.Json.JsonSerializer.Deserialize<AuthenticatorAssertionRawResponse>(credential.ToString())
                ?? throw new AccountSecurityException("无效的通行密钥响应");
            var id = Convert.ToBase64String(response.RawId);
            var owner = IConfigBase.UserList.GetAllUsers().FirstOrDefault(u => u?.Authentication?.Passkeys.Any(p => p.Id == id && p.RpId == rpId) == true)
                ?? throw new AccountSecurityException("通行密钥不存在或已删除", 404);
            var stored = owner.Authentication!.Passkeys.First(p => p.Id == id && p.RpId == rpId);
            var result = await Fido(origin, rpId).MakeAssertionAsync(new MakeAssertionParams
            {
                AssertionResponse = response, OriginalOptions = AssertionOptions.FromJson(pending.Payload),
                StoredPublicKey = Convert.FromBase64String(stored.PublicKey), StoredSignatureCounter = stored.SignCount,
                IsUserHandleOwnerOfCredentialIdCallback = (args, _) => Task.FromResult(
                    args.UserHandle.SequenceEqual(Encoding.UTF8.GetBytes(owner.Id)) && args.CredentialId.SequenceEqual(response.RawId))
            });
            return IConfigBase.UserList.MutateAuthentication(owner.Id, u =>
            {
                EnsureVersion(u, owner.TokenVersion);
                var key = u.Authentication?.Passkeys.FirstOrDefault(p => p.Id == id && p.RpId == rpId)
                    ?? throw new AccountSecurityException("通行密钥已删除", 401);
                if (key.PublicKey != stored.PublicKey || key.SignCount != stored.SignCount || key.IsBackupEligible != stored.IsBackupEligible)
                    throw new AccountSecurityException("通行密钥状态已变更，请重试", 400);
                // Backup eligibility cannot change. Fido validates BS/BE consistency; enforce the stored BE flag as well.
                var flags = response.Response.AuthenticatorData[32];
                if (((flags & 8) != 0) != key.IsBackupEligible) throw new AccountSecurityException("通行密钥备份状态无效", 400);
                key.SignCount = result.SignCount;
                key.IsBackedUp = result.IsBackedUp;
                key.LastUsedAt = Now.UtcDateTime;
            });
        }
        catch (Exception e) when (e is not IOException and not UnauthorizedAccessException)
        {
            FailIp(request.HttpContext);
            throw;
        }
    }

    public object DeletePasskey(UserInfo user, string password, string? code, string id)
    {
        var current = Management(user, password, code, u =>
        {
            // IDs exposed by the management DTO are base64url to remain safe in URL path segments.
            if (u.Authentication?.Passkeys.RemoveAll(p => UrlId(p.Id) == id) is not > 0)
                throw new AccountSecurityException("通行密钥不存在", 404);
            u.TokenVersion++;
        });
        return new { token = JwtUtils.GenerateToken(current) };
    }

    public static string UrlId(string base64) => base64.TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private string Store(Pending pending)
    {
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        lock (_gate) cache.Set("CEREMONY_" + id, pending with { ExpiresAt = Now.Add(Lifetime) }, Lifetime);
        return id;
    }

    private Pending Take(string id, string purpose, string origin, string? userId, bool consume = true)
    {
        lock (_gate)
        {
            var key = "CEREMONY_" + id;
            if (id.Length > 128 || !cache.TryGetValue<Pending>(key, out var pending) || pending == null || pending.ExpiresAt <= Now)
                throw new AccountSecurityException("验证请求已失效，请重新开始");
            if (consume) cache.Remove(key);
            if (pending.Purpose != purpose || pending.Origin != origin || pending.UserId != userId)
                throw new AccountSecurityException("验证请求不匹配", 403);
            return pending;
        }
    }

    private void LimitOptions(HttpContext context)
    {
        lock (_gate)
        {
            var key = "AUTH_OPTIONS_" + context.Connection.RemoteIpAddress;
            cache.TryGetValue(key, out int count);
            if (count >= 30) throw new AccountSecurityException("请求过于频繁，请稍后重试", 429);
            cache.Set(key, count + 1, TimeSpan.FromMinutes(1));
        }
    }

    private void CheckFailures(string key)
    {
        if (cache.TryGetValue<Failures>(key, out var failed) && failed != null && failed.Count >= 10 && failed.Until > Now)
            throw new AccountSecurityException("验证错误次数过多，请于 60 分钟后再试", 403);
    }

    private void Fail(string key)
    {
        cache.TryGetValue<Failures>(key, out var failed);
        var count = failed != null && failed.Until > Now ? failed.Count + 1 : 1;
        var until = Now.AddMinutes(count >= 10 ? 60 : 5);
        cache.Set(key, new Failures(count, until), until);
    }

    public void CheckIp(HttpContext context)
    {
        lock (_gate)
            if (cache.TryGetValue("BAN_" + context.Connection.RemoteIpAddress, out _))
                throw new AccountSecurityException("您的 IP 已被暂时封禁，请于 60 分钟后再试", 403);
    }

    public void FailIp(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress;
        if (ip == null || IPAddress.IsLoopback(ip)) return;
        lock (_gate)
        {
            var key = "ERR_COUNT_" + ip;
            cache.TryGetValue(key, out int count);
            if (count + 1 >= 10)
            {
                cache.Set("BAN_" + ip, true, TimeSpan.FromMinutes(60));
                cache.Remove(key);
            }
            else cache.Set(key, count + 1, Lifetime);
        }
    }
}
