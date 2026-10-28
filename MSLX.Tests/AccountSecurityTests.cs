using System.Formats.Cbor;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using MSLX.Daemon.Controllers.AuthControllers;
using MSLX.Daemon.Services;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.Models;
using Newtonsoft.Json.Linq;
using OtpNet;

namespace MSLX.Tests;

[Collection("GlobalConfig")]
public sealed class AccountSecurityTests : IDisposable
{
    private const string Password = "Security-test-123!";
    private const string Origin = "http://localhost:17800";
    private readonly string _path;
    private readonly string? _original;
    private readonly UserListConfig? _previous;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly string _keys = Path.Combine(Path.GetTempPath(), "mslx-security-tests-" + Guid.NewGuid().ToString("N"));
    private readonly TestClock _clock = new();
    private readonly AccountSecurityService _service;
    private readonly UserInfo _user;
    private UserInfo Current => IConfigBase.UserList.GetUserById(_user.Id)!;

    public AccountSecurityTests()
    {
        IConfigBase.Initialize(NullLoggerFactory.Instance);
        _path = Path.Combine(IConfigBase.GetAppConfigPath(), "UserList.json");
        _original = File.Exists(_path) ? File.ReadAllText(_path) : null;
        _previous = IConfigBase.UserList;
        IConfigBase.UserList = new UserListConfig();
        _user = new UserInfo { Username = "security-" + Guid.NewGuid().ToString("N"), PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password, 4), Role = "admin", Name = "Security QA" };
        IConfigBase.UserList.CreateUser(_user);
        _service = new(_cache, DataProtectionProvider.Create(new DirectoryInfo(_keys)), _clock);
    }

    public void Dispose()
    {
        IConfigBase.UserList.Dispose();
        IConfigBase.UserList = _previous!;
        if (_original == null) File.Delete(_path); else File.WriteAllText(_path, _original);
        _cache.Dispose();
        if (Directory.Exists(_keys)) Directory.Delete(_keys, true);
    }
    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Time = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Time;
    }
    private static DefaultHttpContext Http(string origin = Origin)
    {
        var uri = new Uri(origin);
        var http = new DefaultHttpContext();
        http.Request.Scheme = uri.Scheme; http.Request.Host = new HostString(uri.Authority);
        http.Request.Headers.Origin = origin; http.Connection.RemoteIpAddress = IPAddress.Loopback;
        return http;
    }
    private string Code(string secret, int shift = 0) => new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(_clock.Time.AddSeconds(shift).UtcDateTime);
    private (string Secret, string[] Codes) Enable()
    {
        var setup = JObject.FromObject(_service.SetupTotp(Current, Password));
        var secret = setup.Value<string>("secret")!;
        var result = JObject.FromObject(_service.EnableTotp(Current, Password, setup.Value<string>("setupId")!, Code(secret)));
        return (secret, result["recoveryCodes"]!.ToObject<string[]>()!);
    }

    [Fact]
    public void LegacyLogin_AndTwoFactorHintPreserveContractWithoutPrematureSideEffects()
    {
        var controller = new AuthController(_cache, _service) { ControllerContext = new ControllerContext { HttpContext = Http() } };
        var response = Assert.IsType<OkObjectResult>(controller.Login(new LoginRequest { Username = _user.Username, Password = Password }));
        Assert.NotNull(JObject.FromObject(response.Value!)["Data"]?["token"]);
        var (secret, _) = Enable();
        var before = Current.LastLoginTime;
        response = Assert.IsType<OkObjectResult>(controller.Login(new LoginRequest { Username = _user.Username, Password = Password }));
        var data = JObject.FromObject(response.Value!)["Data"]!;
        Assert.True(data.Value<bool>("requiresTwoFactor")); Assert.Null(data["token"]);
        Assert.Equal(before, Current.LastLoginTime);
        Assert.IsType<ObjectResult>(controller.Login(new LoginRequest { Username = _user.Username, Password = Password, TwoFactorCode = Code(secret) }));
        _clock.Time = _clock.Time.AddSeconds(30);
        Assert.IsType<OkObjectResult>(controller.Login(new LoginRequest { Username = _user.Username, Password = Password, TwoFactorCode = Code(secret) }));
    }

    [Fact]
    public void Totp_EncryptsSecretRejectsOldAndReplayCodes_AndPersistsKeyRing()
    {
        var (secret, _) = Enable();
        Assert.DoesNotContain(secret, File.ReadAllText(_path));
        Assert.Throws<AccountSecurityException>(() => _service.VerifyLogin(Current, Code(secret)));
        Assert.Throws<AccountSecurityException>(() => _service.VerifyLogin(Current, Code(secret, -90)));
        _clock.Time = _clock.Time.AddSeconds(30);
        var restarted = new AccountSecurityService(_cache, DataProtectionProvider.Create(new DirectoryInfo(_keys)), _clock);
        var stale = Current;
        restarted.VerifyLogin(stale, Code(secret));
        Assert.Throws<AccountSecurityException>(() => restarted.VerifyLogin(stale, Code(secret)));
        Assert.True(AccountSecurityService.HasTotp(Current));
    }

    [Fact]
    public async Task RecoveryCode_IsConsumedExactlyOnceAcrossConcurrentRequests()
    {
        var (_, codes) = Enable(); var stale = Current;
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() =>
        { try { _service.VerifyLogin(stale, codes[0]); return true; } catch (AccountSecurityException) { return false; } })));
        Assert.Single(results, ok => ok);
        Assert.Equal(9, Current.Authentication!.RecoveryCodeHashes.Count);
        Assert.DoesNotContain(codes[0], File.ReadAllText(_path));
    }

    [Fact]
    public void Totp_AccountLockoutAppliesOnLoopback_AndExpires()
    {
        var (secret, _) = Enable();
        for (int i = 0; i < 10; i++) Assert.Throws<AccountSecurityException>(() => _service.VerifyLogin(Current, "invalid-code"));
        _clock.Time = _clock.Time.AddSeconds(30);
        Assert.Equal(403, Assert.Throws<AccountSecurityException>(() => _service.VerifyLogin(Current, Code(secret))).Status);
        _clock.Time = _clock.Time.AddMinutes(61);
        _service.VerifyLogin(Current, Code(secret));
    }

    [Fact]
    public void ProfileUpdatePreservesConsumedCodes_AndAdminResetRevokesExistingSessions()
    {
        var stale = Current; var (_, codes) = Enable();
        var token = JwtUtils.GenerateToken(Current);
        _service.VerifyLogin(Current, codes[0]);
        stale.Name = "New display name";
        Assert.True(IConfigBase.UserList.UpdateUser(stale));
        Assert.True(AccountSecurityService.HasTotp(Current));
        Assert.Equal(9, Current.Authentication!.RecoveryCodeHashes.Count);
        var controller = new AdminUserController();
        Assert.IsType<OkObjectResult>(controller.UpdateUser(_user.Id, new AdminUpdateUserRequest { ResetTotp = true }));
        Assert.False(AccountSecurityService.HasTotp(Current));
        Assert.Empty(Current.Authentication.RecoveryCodeHashes);
        Assert.NotNull(JwtUtils.GetTokenRejectionReason(JwtUtils.ValidateToken(token)!, Current));
    }

    [Fact]
    public void FailedSave_DoesNotConsumeRecoveryOrPublishProfileChanges()
    {
        var (_, codes) = Enable();
        var original = File.ReadAllText(_path);
        File.Delete(_path); Directory.CreateDirectory(_path);
        try
        {
            Assert.ThrowsAny<IOException>(() => _service.VerifyLogin(Current, codes[0]));
            Assert.Equal(10, Current.Authentication!.RecoveryCodeHashes.Count);
            var stale = Current; stale.Name = "Must not publish";
            Assert.ThrowsAny<IOException>(() => IConfigBase.UserList.UpdateUser(stale));
            Assert.NotEqual("Must not publish", Current.Name);
        }
        finally { Directory.Delete(_path); File.WriteAllText(_path, original); }
        _service.VerifyLogin(Current, codes[0]);
    }

    [Fact]
    public void Setup_RequiresPasswordExpiresAndDoesNotEnableUntilConfirmed()
    {
        Assert.Throws<AccountSecurityException>(() => _service.SetupTotp(Current, "wrong"));
        var setup = JObject.FromObject(_service.SetupTotp(Current, Password));
        Assert.False(AccountSecurityService.HasTotp(Current));
        _clock.Time = _clock.Time.AddMinutes(6);
        Assert.Throws<AccountSecurityException>(() => _service.EnableTotp(Current, Password, setup.Value<string>("setupId")!, Code(setup.Value<string>("secret")!)));
        Assert.False(AccountSecurityService.HasTotp(Current));
    }

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://127.0.0.1")]
    [InlineData("http://192.168.0.1")]
    public void Passkey_RejectsInsecureOrIpOrigins(string origin) => Assert.Throws<AccountSecurityException>(() => _service.LoginOptions(Http(origin).Request));

    [Fact]
    public void Passkey_RejectsCrossOrigin_AndDoesNotTrustPayloadOrForwardingHeaders()
    {
        var http = Http(); http.Request.Headers.Origin = "https://evil.example";
        Assert.Throws<AccountSecurityException>(() => _service.LoginOptions(http.Request));
        http = Http("http://example.com"); http.Request.Headers["X-Forwarded-Proto"] = "https";
        Assert.Throws<AccountSecurityException>(() => _service.LoginOptions(http.Request));
    }

    [Fact]
    public void Passkey_HttpsSiteWithHttpOriginConnection_NeedsNoForwardedHeaders()
    {
        var http = Http("https://panel.example:8443");
        http.Request.Scheme = "http"; // CDN HTTPS frontend, plain HTTP origin connection.
        var site = AccountSecurityService.Site(http.Request);
        Assert.Equal("https://panel.example:8443", site.Origin);
        Assert.Equal("panel.example", site.RpId);
        var options = JObject.FromObject(_service.LoginOptions(http.Request));
        Assert.Equal("panel.example", options["options"]?["rpId"]?.ToString());
        http.Request.Headers.Origin = "https://evil.example:8443";
        Assert.Throws<AccountSecurityException>(() => AccountSecurityService.Site(http.Request));
        http.Request.Headers.Origin = "https://panel.example:9443";
        Assert.Throws<AccountSecurityException>(() => AccountSecurityService.Site(http.Request));
        http.Request.Headers.Origin = "http://panel.example:8443";
        Assert.Throws<AccountSecurityException>(() => AccountSecurityService.Site(http.Request));
        // Same-origin GET requests may omit Origin; availability must still use the public HTTPS site.
        http.Request.Headers.Remove("Origin");
        Assert.Equal(site, AccountSecurityService.Site(http.Request));
    }

    [Fact]
    public async Task Passkey_HttpsRegistrationAndLoginOverHttpOriginConnection_StillValidateSignedOrigin()
    {
        const string publicOrigin = "https://panel.example:8443";
        DefaultHttpContext BehindCdn()
        {
            var context = Http(publicOrigin);
            context.Request.Scheme = "http";
            return context;
        }
        using var auth = new SoftwareAuthenticator();
        var registration = JObject.FromObject(_service.RegisterOptions(BehindCdn().Request, Current, Password, null, "CDN key"));
        await _service.Register(BehindCdn().Request, Current, registration.Value<string>("challengeId")!,
            auth.Register(registration["options"]!, origin: publicOrigin, rp: "panel.example"));
        var get = BehindCdn(); get.Request.Headers.Remove("Origin");
        Assert.True(AccountSecurityService.HasPasskeysForSite(get.Request));
        var options = JObject.FromObject(_service.LoginOptions(BehindCdn().Request));
        var user = await _service.LoginPasskey(BehindCdn().Request, options.Value<string>("challengeId")!,
            auth.Assert(options["options"]!, _user.Id, origin: publicOrigin, rp: "panel.example"));
        Assert.Equal(_user.Id, user.Id);
        foreach (var signedOrigin in new[] { "https://evil.example:8443", "http://panel.example:8443", "https://panel.example:9443" })
        {
            options = JObject.FromObject(_service.LoginOptions(BehindCdn().Request));
            await Assert.ThrowsAnyAsync<Exception>(() => _service.LoginPasskey(BehindCdn().Request, options.Value<string>("challengeId")!,
                auth.Assert(options["options"]!, _user.Id, origin: signedOrigin, rp: "panel.example")));
        }
    }

    [Fact]
    public async Task Passkey_FullCeremonyRequiresUV_ReplayFails_RenameWorksAndDeletionRevokes()
    {
        Assert.False(AccountSecurityService.HasPasskeysForSite(Http().Request));
        using var authenticator = new SoftwareAuthenticator();
        var register = JObject.FromObject(_service.RegisterOptions(Http().Request, Current, Password, null, "Test key"));
        Assert.Equal("required", register["options"]?["authenticatorSelection"]?["userVerification"]?.ToString());
        Assert.Equal("required", register["options"]?["authenticatorSelection"]?["residentKey"]?.ToString());
        var registration = authenticator.Register(register["options"]!);
        await _service.Register(Http().Request, Current, register.Value<string>("challengeId")!, registration);
        Assert.Single(Current.Authentication!.Passkeys);
        Assert.True(AccountSecurityService.HasPasskeysForSite(Http().Request));
        Assert.False(AccountSecurityService.HasPasskeysForSite(Http("https://other.example").Request));
        Assert.False(AccountSecurityService.HasPasskeysForSite(Http("http://192.168.1.2").Request));
        var crossOrigin = Http(); crossOrigin.Request.Headers.Origin = "https://other.example";
        Assert.False(AccountSecurityService.HasPasskeysForSite(crossOrigin.Request));
        await Assert.ThrowsAsync<AccountSecurityException>(() => _service.Register(Http().Request, Current, register.Value<string>("challengeId")!, registration));
        var stale = Current; stale.Username += "-renamed"; IConfigBase.UserList.UpdateUser(stale);
        Enable(); // A UV-verified passkey remains an independent login method even with TOTP enabled.
        var options = JObject.FromObject(_service.LoginOptions(Http().Request));
        var assertion = authenticator.Assert(options["options"]!, _user.Id);
        var authenticated = await _service.LoginPasskey(Http().Request, options.Value<string>("challengeId")!, assertion);
        Assert.Equal(stale.Username, authenticated.Username);
        Assert.NotNull(authenticated.Authentication!.Passkeys[0].LastUsedAt);
        var data = JObject.FromObject(AccountSecurityService.CompleteLogin(authenticated));
        Assert.NotNull(data["token"]);
        await Assert.ThrowsAsync<AccountSecurityException>(() => _service.LoginPasskey(Http().Request, options.Value<string>("challengeId")!, assertion));
        var deletionOptions = JObject.FromObject(_service.LoginOptions(Http().Request));
        var deletionAssertion = authenticator.Assert(deletionOptions["options"]!, _user.Id);
        // Admin reset retains passkeys, enabling password proof for their deletion.
        IConfigBase.UserList.UpdateUser(Current, true);
        _service.DeletePasskey(Current, Password, null, AccountSecurityService.UrlId(Current.Authentication!.Passkeys[0].Id));
        Assert.Empty(Current.Authentication.Passkeys);
        Assert.False(AccountSecurityService.HasPasskeysForSite(Http().Request));
        Assert.Equal(404, (await Assert.ThrowsAsync<AccountSecurityException>(() => _service.LoginPasskey(Http().Request, deletionOptions.Value<string>("challengeId")!, deletionAssertion))).Status);
    }

    [Theory]
    [InlineData("uv")]
    [InlineData("origin")]
    [InlineData("rp")]
    [InlineData("owner")]
    [InlineData("signature")]
    public async Task Passkey_RejectsInvalidAssertions(string failure)
    {
        using var auth = new SoftwareAuthenticator();
        var register = JObject.FromObject(_service.RegisterOptions(Http().Request, Current, Password, null, "Test key"));
        await _service.Register(Http().Request, Current, register.Value<string>("challengeId")!, auth.Register(register["options"]!));
        var options = JObject.FromObject(_service.LoginOptions(Http().Request));
        var assertion = auth.Assert(options["options"]!, failure == "owner" ? "another-user" : _user.Id,
            uv: failure != "uv", origin: failure == "origin" ? "https://evil.example" : Origin,
            rp: failure == "rp" ? "evil.example" : "localhost");
        if (failure == "signature") assertion["response"]!["signature"] = Url(RandomNumberGenerator.GetBytes(64));
        await Assert.ThrowsAnyAsync<Exception>(() => _service.LoginPasskey(Http().Request, options.Value<string>("challengeId")!, assertion));
        Assert.Null(Current.Authentication!.Passkeys[0].LastUsedAt);
    }

    [Fact]
    public async Task Passkey_ChallengeExpiryAccountDeletionAndCounterlessSyncedKeys()
    {
        using var auth = new SoftwareAuthenticator();
        var register = JObject.FromObject(_service.RegisterOptions(Http().Request, Current, Password, null, "Synced key"));
        await _service.Register(Http().Request, Current, register.Value<string>("challengeId")!, auth.Register(register["options"]!, backup: true));
        for (int i = 0; i < 2; i++)
        {
            var options = JObject.FromObject(_service.LoginOptions(Http().Request));
            await _service.LoginPasskey(Http().Request, options.Value<string>("challengeId")!, auth.Assert(options["options"]!, _user.Id, backup: true));
        }
        var expired = JObject.FromObject(_service.LoginOptions(Http().Request));
        _clock.Time = _clock.Time.AddMinutes(6);
        await Assert.ThrowsAsync<AccountSecurityException>(() => _service.LoginPasskey(Http().Request, expired.Value<string>("challengeId")!, auth.Assert(expired["options"]!, _user.Id, backup: true)));
        var deleted = JObject.FromObject(_service.LoginOptions(Http().Request));
        IConfigBase.UserList.DeleteUser(_user.Id);
        Assert.Equal(404, (await Assert.ThrowsAsync<AccountSecurityException>(() => _service.LoginPasskey(Http().Request, deleted.Value<string>("challengeId")!, auth.Assert(deleted["options"]!, _user.Id, backup: true)))).Status);
    }

    [Fact]
    public void Totp_SetupCanRetryInvalidCode_AndAcceptsClockSkewWithoutReplay()
    {
        var setup = JObject.FromObject(_service.SetupTotp(Current, Password));
        var secret = setup.Value<string>("secret")!;
        var id = setup.Value<string>("setupId")!;
        Assert.Throws<AccountSecurityException>(() => _service.EnableTotp(Current, Password, id, "bad-code"));
        _service.EnableTotp(Current, Password, id, Code(secret));
        _clock.Time = _clock.Time.AddSeconds(60);
        _service.VerifyLogin(Current, Code(secret, -30));
        _service.VerifyLogin(Current, Code(secret, 30));
        Assert.Throws<AccountSecurityException>(() => _service.VerifyLogin(Current, Code(secret)));
    }

    [Fact]
    public async Task Passkey_RegistrationRejectsMissingUV_AndSecurityChangesInvalidateProof()
    {
        using var auth = new SoftwareAuthenticator();
        var register = JObject.FromObject(_service.RegisterOptions(Http().Request, Current, Password, null, "UV required"));
        await Assert.ThrowsAnyAsync<Exception>(() => _service.Register(Http().Request, Current, register.Value<string>("challengeId")!, auth.Register(register["options"]!, uv: false)));
        Assert.Empty(Current.Authentication?.Passkeys ?? []);
        register = JObject.FromObject(_service.RegisterOptions(Http().Request, Current, Password, null, "Stale proof"));
        IConfigBase.UserList.UpdateUser(Current, true);
        await Assert.ThrowsAsync<AccountSecurityException>(() => _service.Register(Http().Request, Current, register.Value<string>("challengeId")!, auth.Register(register["options"]!)));
        Assert.Empty(Current.Authentication?.Passkeys ?? []);
    }

    [Fact]
    public void PasswordResetBetweenPasswordVerificationAndCompletionCannotIssueSession()
    {
        var stale = Current;
        Assert.True(AccountSecurityService.PasswordMatches(stale, Password));
        var reset = Current; reset.PasswordHash = BCrypt.Net.BCrypt.HashPassword("New-password-456!", 4);
        IConfigBase.UserList.UpdateUser(reset);
        Assert.Throws<AccountSecurityException>(() => AccountSecurityService.CompleteLogin(stale));
        Assert.Null(Current.LastLoginTime);
    }

    [Fact]
    public async Task Passkey_SignatureCounterMustAdvanceAndPersist()
    {
        using var auth = new SoftwareAuthenticator();
        var register = JObject.FromObject(_service.RegisterOptions(Http().Request, Current, Password, null, "Counter key"));
        await _service.Register(Http().Request, Current, register.Value<string>("challengeId")!, auth.Register(register["options"]!, counter: 1));
        var options = JObject.FromObject(_service.LoginOptions(Http().Request));
        await Assert.ThrowsAnyAsync<Exception>(() => _service.LoginPasskey(Http().Request, options.Value<string>("challengeId")!, auth.Assert(options["options"]!, _user.Id, counter: 1)));
        options = JObject.FromObject(_service.LoginOptions(Http().Request));
        await _service.LoginPasskey(Http().Request, options.Value<string>("challengeId")!, auth.Assert(options["options"]!, _user.Id, counter: 2));
        Assert.Equal(2u, Current.Authentication!.Passkeys[0].SignCount);
        options = JObject.FromObject(_service.LoginOptions(Http().Request));
        await Assert.ThrowsAnyAsync<Exception>(() => _service.LoginPasskey(Http().Request, options.Value<string>("challengeId")!, auth.Assert(options["options"]!, _user.Id, counter: 2)));
    }

    private static string Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    // Test-only authenticator: actual ECDSA signatures and WebAuthn CBOR, no verifier mocks.
    private sealed class SoftwareAuthenticator : IDisposable
    {
        private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly byte[] _id = RandomNumberGenerator.GetBytes(32);
        public void Dispose() => _key.Dispose();
        private static byte[] Counter(uint value)
        {
            var bytes = new byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes, value); return bytes;
        }
        private static byte[] Client(string type, JToken options, string origin) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        { type, challenge = options.Value<string>("challenge"), origin, crossOrigin = false });
        public JObject Register(JToken options, bool backup = false, bool uv = true, uint counter = 0, string origin = Origin, string rp = "localhost")
        {
            var parameters = _key.ExportParameters(false);
            var cose = new CborWriter(); cose.WriteStartMap(5);
            cose.WriteInt32(1); cose.WriteInt32(2); cose.WriteInt32(3); cose.WriteInt32(-7);
            cose.WriteInt32(-1); cose.WriteInt32(1); cose.WriteInt32(-2); cose.WriteByteString(parameters.Q.X!);
            cose.WriteInt32(-3); cose.WriteByteString(parameters.Q.Y!); cose.WriteEndMap();
            var data = SHA256.HashData(Encoding.UTF8.GetBytes(rp))
                .Concat(new byte[] { (byte)((uv ? 0x45 : 0x41) | (backup ? 0x18 : 0)) }).Concat(Counter(counter)).Concat(new byte[16])
                .Concat(new byte[] { 0, (byte)_id.Length }).Concat(_id).Concat(cose.Encode()).ToArray();
            var att = new CborWriter(); att.WriteStartMap(3);
            att.WriteTextString("fmt"); att.WriteTextString("none"); att.WriteTextString("authData"); att.WriteByteString(data);
            att.WriteTextString("attStmt"); att.WriteStartMap(0); att.WriteEndMap(); att.WriteEndMap();
            return JObject.FromObject(new { id = Url(_id), rawId = Url(_id), type = "public-key", response = new
            { clientDataJSON = Url(Client("webauthn.create", options, origin)), attestationObject = Url(att.Encode()), transports = new[] { "internal" } } });
        }
        public JObject Assert(JToken options, string userId, bool uv = true, string origin = Origin, string rp = "localhost", bool backup = false, uint counter = 0)
        {
            var client = Client("webauthn.get", options, origin);
            var data = SHA256.HashData(Encoding.UTF8.GetBytes(rp)).Concat(new byte[] { (byte)((uv ? 5 : 1) | (backup ? 0x18 : 0)) }).Concat(Counter(counter)).ToArray();
            var signature = _key.SignData(data.Concat(SHA256.HashData(client)).ToArray(), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
            return JObject.FromObject(new { id = Url(_id), rawId = Url(_id), type = "public-key", response = new
            { clientDataJSON = Url(client), authenticatorData = Url(data), signature = Url(signature), userHandle = Url(Encoding.UTF8.GetBytes(userId)) } });
        }
    }
}
