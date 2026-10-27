using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using MSLX.Daemon.Controllers.NodesControllers;
using MSLX.Daemon.Middleware;
using MSLX.Daemon.Services.NodeServices;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.Models;
using MSLX.SDK.Models.Node;
using Newtonsoft.Json.Linq;

namespace MSLX.Tests;

[Collection("GlobalConfig")]
public sealed class NodeApiKeyTests : IDisposable
{
    private readonly UserListConfig _previousUsers;
    private readonly NodeListConfig _previousNodes;
    private readonly JObject _config;
    private readonly Dictionary<string, string?> _files;
    private readonly UserInfo _user = new() { Id = "api-key-user", Username = "api-key-test", Role = "admin", ApiKey = "test-user-api-key", PasswordHash = "test-password" };
    private readonly SlaveNodeConfig _node = new() { NodeId = "api-key-node", CommsKey = "test-node-comms-secret" };

    public NodeApiKeyTests()
    {
        IConfigBase.Initialize(NullLoggerFactory.Instance);
        _config = IConfigBase.Config.ReadConfig();
        _previousUsers = IConfigBase.UserList;
        _previousNodes = IConfigBase.NodeList;
        _files = new[] { "UserList.json", "Nodes.json" }.ToDictionary(
            name => Path.Combine(IConfigBase.GetAppConfigPath(), name),
            name => File.Exists(Path.Combine(IConfigBase.GetAppConfigPath(), name))
                ? File.ReadAllText(Path.Combine(IConfigBase.GetAppConfigPath(), name)) : null);
        IConfigBase.UserList = new UserListConfig();
        IConfigBase.NodeList = new NodeListConfig();
        IConfigBase.UserList.DeleteUser(_user.Id);
        Assert.True(IConfigBase.UserList.CreateUser(_user));
        IConfigBase.NodeList.AddOrUpdateNode(_node);
        IConfigBase.Config.WriteConfigKey("apiKey", "test-global-api-key");
        IConfigBase.Config.WriteConfigKey("IsSlaveMode", false);
    }

    private static MasterNodeController Controller(ClaimsPrincipal? principal = null) =>
        new(NullLogger<MasterNodeController>.Instance)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal ?? new ClaimsPrincipal() } } };

    private static async Task<DefaultHttpContext> Authenticate(string key, string path = "/api/node/session", string? token = null, bool query = false)
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var http = new DefaultHttpContext();
        http.Request.Path = path;
        http.Response.Body = new MemoryStream();
        if (query) http.Request.QueryString = new QueryString("?x-api-key=" + key);
        else http.Request.Headers["x-api-key"] = key;
        if (token != null) http.Request.Headers["x-user-token"] = token;
        await new AuthMiddleware(_ => Task.CompletedTask, cache).InvokeAsync(http, new NodeAuthorization(cache));
        return http;
    }

    private static string Token(IActionResult result) => JObject.FromObject(Assert.IsType<OkObjectResult>(result).Value!)["Data"]!["token"]!.ToString();

    [Theory]
    [InlineData(false, null, 600)] [InlineData(true, null, 600)]
    [InlineData(false, "media", 86400)] [InlineData(true, "media", 86400)]
    [InlineData(false, "download", 7200)] [InlineData(true, "download", 7200)]
    public async Task ApiKeys_IssueOnlyNodeBoundPurposeCredentials(bool global, string? scope, int lifetime)
    {
        var key = global ? "test-global-api-key" : _user.ApiKey;
        var http = await Authenticate(key);
        Assert.Equal(200, http.Response.StatusCode);
        Assert.NotNull(NodeCallerAuthorization.GetCaller(http.User));
        var token = Token(Controller(http.User).CreateSession(new NodeSessionRequest { NodeId = _node.NodeId, Scope = scope }));
        var principal = JwtUtils.ValidateNodeToken(token, _node)!;
        Assert.Equal(scope, principal.FindFirst("Scope")?.Value);
        Assert.Equal(global ? "system-admin" : _user.Id, principal.FindFirst("UserId")!.Value);
        Assert.Null(principal.FindFirst("ParentJti"));
        Assert.InRange(long.Parse(principal.FindFirst("exp")!.Value) - DateTimeOffset.UtcNow.ToUnixTimeSeconds(), lifetime - 5, lifetime);
        Assert.DoesNotContain(key, new JwtSecurityTokenHandler().ReadJwtToken(token).Payload.SerializeToJson());
        Assert.DoesNotContain(key, string.Join(";", http.User.Claims.Select(c => c.Value)));
        Assert.Null(JwtUtils.ValidateToken(token));
        Assert.Null(JwtUtils.ValidateNodeToken(token, new SlaveNodeConfig { NodeId = "another-node", CommsKey = _node.CommsKey }));
        Assert.IsType<OkObjectResult>(Controller().VerifyToken(new VerifyTokenRequest { Token = token }, _node.CommsKey));
        Assert.Null(NodeCallerAuthorization.GetCaller(principal));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task RotatedOrClearedKey_RevokesEveryScopeAndCapturedTaskIdentity(bool global)
    {
        var http = await Authenticate(global ? "test-global-api-key" : _user.ApiKey);
        var tokens = new[] { "", "media", "download" }.Select(scope =>
            Token(Controller(http.User).CreateSession(new NodeSessionRequest { NodeId = _node.NodeId, Scope = scope }))).ToArray();
        if (global) IConfigBase.Config.WriteConfigKey("apiKey", "rotated-global-api-key");
        else { _user.ApiKey = "rotated-user-api-key"; IConfigBase.UserList.UpdateUser(_user); }
        Assert.Null(NodeCallerAuthorization.GetCaller(http.User));
        Assert.IsType<UnauthorizedObjectResult>(Controller(http.User).CreateSession(new NodeSessionRequest { NodeId = _node.NodeId }));
        foreach (var token in tokens)
            Assert.IsType<UnauthorizedObjectResult>(Controller().VerifyToken(new VerifyTokenRequest { Token = token }, _node.CommsKey));
        var fresh = await Authenticate(global ? "rotated-global-api-key" : _user.ApiKey);
        Assert.NotNull(NodeCallerAuthorization.GetCaller(fresh.User));
        var renewed = Token(Controller(fresh.User).CreateSession(new NodeSessionRequest { NodeId = _node.NodeId }));
        if (global) IConfigBase.Config.WriteConfigKey("apiKey", "");
        else { _user.ApiKey = ""; IConfigBase.UserList.UpdateUser(_user); }
        Assert.IsType<UnauthorizedObjectResult>(Controller().VerifyToken(new VerifyTokenRequest { Token = renewed }, _node.CommsKey));
        var sync = new NodeSyncService(null!, null!, null!, NullLogger<NodeSyncService>.Instance);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => sync.PreviewAsync(new NodeSyncRequest(), http.User, CancellationToken.None));
        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
    }

    [Fact]
    public async Task UserKeys_RespectCurrentNodeResourcesDeletionAndLinkRemoval()
    {
        _user.Role = "user";
        IConfigBase.UserList.UpdateUser(_user);
        var http = await Authenticate(_user.ApiKey);
        Assert.IsType<ForbidResult>(Controller(http.User).CreateSession(new NodeSessionRequest { NodeId = _node.NodeId }));
        _user.Resources = [_node.NodeId + "_server:1"];
        IConfigBase.UserList.UpdateUser(_user);
        var token = Token(Controller(http.User).CreateSession(new NodeSessionRequest { NodeId = _node.NodeId }));
        var verified = JObject.FromObject(Assert.IsType<OkObjectResult>(Controller().VerifyToken(new VerifyTokenRequest { Token = token }, _node.CommsKey)).Value!);
        Assert.Equal("server:1", verified["Data"]!["resources"]![0]!.ToString());
        _user.Resources.Clear(); IConfigBase.UserList.UpdateUser(_user);
        Assert.IsType<UnauthorizedObjectResult>(Controller().VerifyToken(new VerifyTokenRequest { Token = token }, _node.CommsKey));
        _user.Resources = [_node.NodeId + "_server:1"]; IConfigBase.UserList.UpdateUser(_user);
        Assert.IsType<OkObjectResult>(Controller().VerifyToken(new VerifyTokenRequest { Token = token }, _node.CommsKey));
        _node.PendingRemoval = true; IConfigBase.NodeList.AddOrUpdateNode(_node);
        Assert.IsType<UnauthorizedObjectResult>(Controller().VerifyToken(new VerifyTokenRequest { Token = token }, _node.CommsKey));
        Assert.IsType<NotFoundResult>(Controller(http.User).CreateSession(new NodeSessionRequest { NodeId = _node.NodeId }));
        _node.PendingRemoval = false; IConfigBase.NodeList.AddOrUpdateNode(_node);
        IConfigBase.UserList.DeleteUser(_user.Id);
        Assert.Null(NodeCallerAuthorization.GetCaller(http.User));
        Assert.IsType<UnauthorizedObjectResult>(Controller().VerifyToken(new VerifyTokenRequest { Token = token }, _node.CommsKey));
    }

    [Fact]
    public async Task Delegation_AcceptsHeaderOnlyAndNeverFallsBackFromRejectedJwtToApiKey()
    {
        foreach (var path in new[] { "/api/node/session", "/api/node/session/", "/api/node/sync", "/api/node/sync/preview" })
        {
            Assert.Equal(401, (await Authenticate(_user.ApiKey, path, query: true)).Response.StatusCode);
            Assert.Equal(401, (await Authenticate(_user.ApiKey, path, token: "broken-jwt")).Response.StatusCode);
        }
        var login = JwtUtils.GenerateToken(_user);
        var http = await Authenticate("test-global-api-key", token: login);
        Assert.NotEqual("ApiKey", http.User.Identity!.AuthenticationType);
        var token = Token(Controller(http.User).CreateSession(new NodeSessionRequest { NodeId = _node.NodeId }));
        Assert.NotNull(JwtUtils.ValidateNodeToken(token, _node)!.FindFirst("ParentJti"));
        // 原有主节点普通 API 的 APIKey 查询参数兼容行为不变。
        Assert.Equal(200, (await Authenticate(_user.ApiKey, "/api/status", query: true)).Response.StatusCode);
    }

    [Fact]
    public async Task MissingUnknownOrMismatchedKeyProof_IsRejectedWithoutGlobalAdminFallback()
    {
        var http = await Authenticate(_user.ApiKey);
        var user = IConfigBase.UserList.GetUserById(_user.Id)!;
        foreach (var claim in new[] { NodeCallerAuthorization.NodeSourceClaim, NodeCallerAuthorization.NodeProofClaim })
        {
            var grant = JwtUtils.ValidateNodeToken(JwtUtils.GenerateNodeToken(user, _node, http.User), _node)!;
            var identity = (ClaimsIdentity)grant.Identity!;
            identity.RemoveClaim(identity.FindFirst(claim)!);
            Assert.NotNull(JwtUtils.GetTokenRejectionReason(grant, user));
            identity.AddClaim(new Claim(claim, "unknown"));
            Assert.NotNull(JwtUtils.GetTokenRejectionReason(grant, user));
        }
        var stale = new ClaimsPrincipal(http.User.Identities.Select(i => i.Clone()));
        var apiIdentity = (ClaimsIdentity)stale.Identity!;
        apiIdentity.RemoveClaim(apiIdentity.FindFirst("ApiKeySource")!);
        apiIdentity.AddClaim(new Claim("ApiKeySource", "global"));
        Assert.Null(NodeCallerAuthorization.GetCaller(stale));
        Assert.Throws<HttpRequestException>(() => JwtUtils.GenerateNodeToken(user, _node, stale));
    }

    public void Dispose()
    {
        IConfigBase.UserList.Dispose(); IConfigBase.NodeList.Dispose();
        IConfigBase.UserList = _previousUsers; IConfigBase.NodeList = _previousNodes;
        foreach (var (path, original) in _files)
            if (original == null) File.Delete(path); else File.WriteAllText(path, original);
        IConfigBase.Config.WriteConfig(_config);
    }
}
