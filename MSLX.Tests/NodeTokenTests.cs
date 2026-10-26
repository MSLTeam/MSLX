using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Logging.Abstractions;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.Models;
using MSLX.SDK.Models.Node;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using MSLX.Daemon.Middleware;
using MSLX.Daemon.Services;
using System.Net;

namespace MSLX.Tests;

[Collection("GlobalConfig")]
public class NodeTokenTests
{
    [Fact]
    public void LegacyRelationship_MissingNodeIdCanBeRepairedButExistingIdentityCannotBeReplaced()
    {
        Setup();
        var previous = IConfigBase.MasterNodes;
        var path = Path.Combine(IConfigBase.GetAppConfigPath(), "Masters.json");
        var original = File.Exists(path) ? File.ReadAllText(path) : null;
        using var masters = new MasterNodesConfig();
        try
        {
            IConfigBase.MasterNodes = masters;
            masters.AddOrUpdateMaster(new MasterNodeInfo { MasterId = "legacy-repair-test", CommsKey = "legacy-comms-proof" });
            var controller = new MSLX.Daemon.Controllers.NodesControllers.SlaveNodeController(
                NullLogger<MSLX.Daemon.Controllers.NodesControllers.SlaveNodeController>.Instance);
            var update = new UpdateLinkRequest { NodeId = "preserved-node", CommsKey = "legacy-comms-proof", MasterUrl = "http://master.test" };
            Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(controller.UpdateLink(update));
            Assert.NotNull(masters.GetMasterById("preserved-node"));
            update.NodeId = "different-node";
            Assert.IsType<Microsoft.AspNetCore.Mvc.UnauthorizedObjectResult>(controller.UpdateLink(update));
        }
        finally
        {
            IConfigBase.MasterNodes = previous;
            if (original == null) File.Delete(path); else File.WriteAllText(path, original);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedRelationshipSave_DoesNotPublishUncommittedAuthorizationState(bool slaveSide)
    {
        Setup();
        var path = Path.Combine(IConfigBase.GetAppConfigPath(), slaveSide ? "Masters.json" : "Nodes.json");
        var original = File.Exists(path) ? File.ReadAllText(path) : null;
        using var nodes = new NodeListConfig();
        using var masters = new MasterNodesConfig();
        var initialCount = slaveSide ? masters.GetAllMasters().Count : nodes.GetAllNodes().Count;
        try
        {
            File.Delete(path);
            Directory.CreateDirectory(path); // Inject a failed atomic replacement after the temporary file was written.
            Assert.ThrowsAny<IOException>(() =>
            {
                if (slaveSide) masters.AddOrUpdateMaster(new MasterNodeInfo { MasterId = "uncommitted", NodeId = "uncommitted" });
                else nodes.AddOrUpdateNode(new SlaveNodeConfig { NodeId = "uncommitted", PendingRemoval = true });
            });
            Assert.Equal(initialCount, slaveSide ? masters.GetAllMasters().Count : nodes.GetAllNodes().Count);
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".*.tmp"));
        }
        finally
        {
            Directory.Delete(path);
            if (original != null) File.WriteAllText(path, original);
        }
    }

    [Fact]
    public async Task MasterNetworkFailure_DoesNotBanRemoteClientAfterRepeatedRequests()
    {
        Setup();
        var previousMode = IConfigBase.Config.ReadConfigKey("IsSlaveMode");
        var previousMasters = IConfigBase.MasterNodes;
        var file = Path.Combine(IConfigBase.GetAppConfigPath(), "Masters.json");
        var previousFile = File.Exists(file) ? File.ReadAllText(file) : null;
        using var masters = new MasterNodesConfig();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        try
        {
            IConfigBase.Config.WriteConfigKey("IsSlaveMode", true);
            IConfigBase.MasterNodes = masters;
            masters.AddOrUpdateMaster(new MasterNodeInfo { MasterId = "unreachable", NodeId = "network-test",
                MasterUrl = "http://127.0.0.1:1", CommsKey = "test-key" });
            var middleware = new AuthMiddleware(_ => throw new Exception("Must not authorize"), cache);
            for (var i = 0; i < 12; i++)
            {
                var http = new DefaultHttpContext();
                http.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
                http.Request.Path = "/api/status";
                http.Request.Headers["x-node-id"] = "network-test";
                http.Request.Headers["x-user-token"] = "node-test-token";
                http.Response.Body = new MemoryStream();
                await middleware.InvokeAsync(http, new NodeAuthorization(cache));
                Assert.Equal(503, http.Response.StatusCode);
            }
            Assert.False(cache.TryGetValue("BAN_203.0.113.7", out _));
            Assert.False(cache.TryGetValue("ERR_COUNT_203.0.113.7", out _));
        }
        finally
        {
            IConfigBase.Config.WriteConfigKey("IsSlaveMode", previousMode ?? false);
            IConfigBase.MasterNodes = previousMasters;
            if (previousFile == null) File.Delete(file); else File.WriteAllText(file, previousFile);
        }
    }

    [Fact]
    public void UpdatingOneHubIdentity_DoesNotMutateCachedOrOtherRequestIdentity()
    {
        var lease = new MSLX.Daemon.Services.NodeAuthorization.Lease(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim("UserId", "node:a:u")], "NodeAuth")), [], DateTimeOffset.UtcNow.AddSeconds(15));
        var connection = lease.CreatePrincipal();
        var request = lease.CreatePrincipal();
        ((ClaimsIdentity)connection.Identity!).RemoveClaim(connection.FindFirst("UserId")!);
        Assert.Equal("node:a:u", lease.Principal.FindFirst("UserId")?.Value);
        Assert.Equal("node:a:u", request.FindFirst("UserId")?.Value);
    }

    private static (UserInfo User, SlaveNodeConfig Node, ClaimsPrincipal Parent) Setup()
    {
        IConfigBase.Initialize(NullLoggerFactory.Instance);
        var user = new UserInfo { Id = "origin-user", Username = "test", Role = "admin" };
        var node = new SlaveNodeConfig { NodeId = "node-a", CommsKey = "node-a-communications-secret" };
        return (user, node, JwtUtils.ValidateToken(JwtUtils.GenerateToken(user))!);
    }

    [Fact]
    public void DelegatedToken_CannotAuthenticateToMasterOrAnotherRelationship()
    {
        var (user, node, parent) = Setup();
        var token = JwtUtils.GenerateNodeToken(user, node, parent);
        Assert.NotNull(JwtUtils.ValidateNodeToken(token, node));
        Assert.Null(JwtUtils.ValidateToken(token));
        Assert.Null(JwtUtils.ValidateNodeToken(token, new SlaveNodeConfig { NodeId = "node-b", CommsKey = node.CommsKey }));
        Assert.Null(JwtUtils.ValidateNodeToken(token, new SlaveNodeConfig { NodeId = node.NodeId, CommsKey = "rotated-secret" }));
        Assert.Null(JwtUtils.ValidateNodeToken(JwtUtils.GenerateToken(user), node));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("media")]
    [InlineData("download")]
    public void DelegatedToken_PreservesPurposeAndParentRevocation(string? scope)
    {
        var (user, node, parent) = Setup();
        var token = JwtUtils.GenerateNodeToken(user, node, parent, scope);
        var principal = JwtUtils.ValidateNodeToken(token, node)!;
        Assert.Equal(scope, principal.FindFirst("Scope")?.Value);
        Assert.Null(JwtUtils.GetTokenRejectionReason(principal, user));
        user.RevokedTokens[parent.FindFirst(JwtRegisteredClaimNames.Jti)!.Value] = DateTime.UtcNow.AddHours(1);
        Assert.NotNull(JwtUtils.GetTokenRejectionReason(principal, user));
    }

    [Fact]
    public void DelegatedToken_ExpiresWithParentAndHonorsUserVersionChanges()
    {
        var (user, node, parent) = Setup();
        var identity = (ClaimsIdentity)parent.Identity!;
        identity.RemoveClaim(identity.FindFirst("exp")!);
        var parentExpiry = DateTimeOffset.UtcNow.AddSeconds(30).ToUnixTimeSeconds();
        identity.AddClaim(new Claim("exp", parentExpiry.ToString()));
        var principal = JwtUtils.ValidateNodeToken(JwtUtils.GenerateNodeToken(user, node, parent), node)!;
        Assert.Equal(parentExpiry, long.Parse(principal.FindFirst("exp")!.Value));
        user.TokenVersion++;
        Assert.NotNull(JwtUtils.GetTokenRejectionReason(principal, user));
    }
}
