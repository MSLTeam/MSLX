using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using MSLX.Daemon.Utils.ConfigUtils;
using Newtonsoft.Json.Linq;

namespace MSLX.Daemon.Services;

/// <summary>子节点授权服务</summary>
public sealed class NodeAuthorization(IMemoryCache cache)
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(3) };
    public sealed record Lease(ClaimsPrincipal Principal, JArray Resources, DateTimeOffset Until)
    {
        public ClaimsPrincipal CreatePrincipal() => new(Principal.Identities.Select(identity => identity.Clone()));
    }

    public async Task<Lease> VerifyAsync(string nodeId, string token, CancellationToken cancellationToken, bool refresh = false)
    {
        var master = IConfigBase.MasterNodes.GetMasterById(nodeId);
        if (master == null || master.NodeId != nodeId)
            throw new HttpRequestException("节点关系已解除或不存在", null, HttpStatusCode.Unauthorized);
        var key = "NODE_AUTH_" + nodeId + "_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        if (!refresh && cache.TryGetValue(key, out Lease? cached) && cached != null && cached.Until > DateTimeOffset.UtcNow)
            return cached;
        // 撤权生效目标不超过约15秒
        var until = DateTimeOffset.UtcNow.AddSeconds(15);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{master.MasterUrl.TrimEnd('/')}/api/node/verify-token");
            request.Headers.Add("x-api-key", master.CommsKey);
            request.Content = JsonContent.Create(new { Token = token });
            using var response = await Client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException("主节点拒绝节点凭据", null, response.StatusCode);
            var body = JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var data = body["data"] ?? throw new HttpRequestException("主节点鉴权响应无效");
            var expiry = DateTimeOffset.FromUnixTimeSeconds(data["expiresAt"]!.Value<long>());
            until = until < expiry ? until : expiry;
            if (until <= DateTimeOffset.UtcNow)
                throw new HttpRequestException("节点凭据已到期", null, HttpStatusCode.Unauthorized);
            var uid = $"node:{nodeId}:{data["userId"]}";
            var resources = (JArray?)data["resources"] ?? [];
            var claims = new List<Claim>
            {
                new("UserId", uid), new("NodeId", nodeId),
                new(ClaimTypes.Role, data["role"]?.ToString() ?? "user")
            };
            if (!string.IsNullOrEmpty(data["scope"]?.ToString())) claims.Add(new("Scope", data["scope"]!.ToString()));
            claims.AddRange(resources.Select(r => new Claim("Resource", r.ToString())));
            var lease = new Lease(new ClaimsPrincipal(new ClaimsIdentity(claims, "NodeAuth")), resources, until);
            cache.Set(key, lease, until);
            return lease;
        }
        catch
        {
            cache.Remove(key);
            throw;
        }
    }

    public static void Apply(HttpContext context, Lease lease)
    {
        context.User = lease.CreatePrincipal();
        context.Items["NodeLease"] = lease;
        IConfigBase.UserList.UpdateShadowUser(context.User.FindFirst("UserId")!.Value,
            context.User.FindFirst(ClaimTypes.Role)!.Value, (JArray)lease.Resources.DeepClone());
    }
}

public sealed class NodeAuthorizationHubFilter(NodeAuthorization authorization) : IHubFilter
{
    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        var http = context.Context.GetHttpContext();
        if (http?.Items["NodeLease"] is NodeAuthorization.Lease lease)
            _ = MonitorAsync(context.Context, http, lease);
        await next(context);
    }

    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext context, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var http = context.Context.GetHttpContext();
        if (http?.Items["NodeLease"] is NodeAuthorization.Lease)
        {
            try
            {
                var lease = await VerifyAsync(http, context.Context.ConnectionAborted);
                Apply(context.Context, http, lease);
                if (context.Context.User?.HasClaim(c => c.Type == "Scope") == true)
                    throw new HubException("降权凭据不允许控制实时连接");
            }
            catch { context.Context.Abort(); throw new HubException("节点授权已失效或主节点不可用"); }
        }
        return await next(context);
    }

    private Task<NodeAuthorization.Lease> VerifyAsync(HttpContext http, CancellationToken ct, bool refresh = false)
    {
        var bearer = http.Request.Headers.Authorization.ToString();
        var token = bearer.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? bearer[7..]
            : http.Request.Query["access_token"].FirstOrDefault()
                ?? http.Request.Headers["x-user-token"].FirstOrDefault() ?? http.Request.Query["x-user-token"].ToString();
        return authorization.VerifyAsync(http.Request.Headers["x-node-id"].FirstOrDefault() ?? http.Request.Query["x-node-id"].ToString(), token, ct, refresh);
    }

    private static void Apply(HubCallerContext caller, HttpContext http, NodeAuthorization.Lease lease)
    {
        // 旧的资源不再接收内容
        if (caller.User?.FindFirst(ClaimTypes.Role)?.Value != lease.Principal.FindFirst(ClaimTypes.Role)?.Value ||
            !caller.User.FindAll("Resource").Select(c => c.Value).Order().SequenceEqual(
                lease.Principal.FindAll("Resource").Select(c => c.Value).Order()))
            throw new UnauthorizedAccessException("节点权限已变化，请重新连接");
        NodeAuthorization.Apply(http, lease);
    }

    private async Task MonitorAsync(HubCallerContext caller, HttpContext http, NodeAuthorization.Lease lease)
    {
        try
        {
            while (!caller.ConnectionAborted.IsCancellationRequested)
            {
                var remaining = lease.Until - DateTimeOffset.UtcNow;
                if (remaining <= TimeSpan.Zero) break;
                await Task.Delay(remaining > TimeSpan.FromSeconds(4) ? remaining - TimeSpan.FromSeconds(4) : TimeSpan.Zero,
                    caller.ConnectionAborted);
                // 续验失败或太慢，不延长旧权限的有效期
                var previousUntil = lease.Until;
                lease = await VerifyAsync(http, caller.ConnectionAborted, true)
                    .WaitAsync(lease.Until - DateTimeOffset.UtcNow, caller.ConnectionAborted);
                Apply(caller, http, lease);
                if (lease.Until <= previousUntil)
                {
                    var tail = lease.Until - DateTimeOffset.UtcNow;
                    if (tail > TimeSpan.Zero) await Task.Delay(tail, caller.ConnectionAborted);
                    break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch { }
        finally { caller.Abort(); }
    }
}
