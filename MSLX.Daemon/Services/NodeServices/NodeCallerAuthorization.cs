using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.Models;

namespace MSLX.Daemon.Services.NodeServices;

public static class NodeCallerAuthorization
{
    private const string SourceClaim = "ApiKeySource";
    private const string ProofClaim = "ApiKeyProof";
    public const string NodeSourceClaim = "NodeApiKeySource";
    public const string NodeProofClaim = "NodeApiKeyProof";

    // 在 APIKey 验证成功时记录指纹，避免换证或后台任务意外使用重置后的新密钥。
    public static void CaptureApiKey(ClaimsPrincipal principal, string apiKey, bool global, int tokenVersion)
    {
        var identity = (ClaimsIdentity)principal.Identity!;
        var source = global ? "global" : "user";
        identity.AddClaim(new Claim(SourceClaim, source));
        identity.AddClaim(new Claim(ProofClaim, Fingerprint(source, principal.FindFirst("UserId")!.Value, apiKey)));
        identity.AddClaim(new Claim("TokenVersion", tokenVersion.ToString()));
    }

    public static UserInfo? GetCaller(ClaimsPrincipal caller)
    {
        if (caller.Identity?.IsAuthenticated != true || caller.Identity.AuthenticationType == "NodeAuth" ||
            caller.HasClaim(c => c.Type is "Scope" or "NodeId")) return null;
        var user = IConfigBase.UserList.GetUserById(caller.FindFirst("UserId")?.Value ?? "");
        if (user == null || JwtUtils.GetTokenRejectionReason(caller, user) != null) return null;
        if (caller.Identity.AuthenticationType == "ApiKey")
            return TryGetApiKey(caller, user, out _) ? user : null;
        return long.TryParse(caller.FindFirst("exp")?.Value, out var exp) &&
            exp > DateTimeOffset.UtcNow.ToUnixTimeSeconds() && caller.FindFirst("jti") != null ? user : null;
    }

    public static IEnumerable<Claim> CreateNodeClaims(ClaimsPrincipal caller, UserInfo user, string nodeId)
    {
        if (!TryGetApiKey(caller, user, out var apiKey))
            throw new HttpRequestException("APIKey 已失效，请检查主节点密钥", null, System.Net.HttpStatusCode.Unauthorized);
        var source = caller.FindFirst(SourceClaim)!.Value;
        // 从同一份已校验密钥快照生成节点指纹，避免密钥重置与签发并发时串用新密钥。
        return [new Claim(NodeSourceClaim, source), new Claim(NodeProofClaim, Fingerprint(source, user.Id, apiKey, nodeId))];
    }

    public static bool IsCurrentNodeGrant(ClaimsPrincipal principal, UserInfo user)
    {
        var source = principal.FindFirst(NodeSourceClaim)?.Value;
        var proof = principal.FindFirst(NodeProofClaim)?.Value;
        if (source == null && proof == null) return true; // 原有登录 JWT 签发的节点凭据。
        var nodeId = principal.FindFirst("NodeId")?.Value;
        var apiKey = CurrentKey(source, user);
        return !string.IsNullOrEmpty(nodeId) && !string.IsNullOrEmpty(apiKey) &&
            Matches(proof, Fingerprint(source!, user.Id, apiKey, nodeId)) &&
            (user.Role == "admin" || user.Resources.Any(r => r.StartsWith(nodeId + "_", StringComparison.Ordinal)));
    }

    private static bool TryGetApiKey(ClaimsPrincipal principal, UserInfo user, out string apiKey)
    {
        var source = principal.FindFirst(SourceClaim)?.Value;
        apiKey = CurrentKey(source, user);
        return principal.Identity?.AuthenticationType == "ApiKey" && !string.IsNullOrEmpty(apiKey) &&
            Matches(principal.FindFirst(ProofClaim)?.Value, Fingerprint(source!, user.Id, apiKey));
    }

    private static string CurrentKey(string? source, UserInfo user) => source switch
    {
        "global" when user.Id == "system-admin" => IConfigBase.Config.ReadConfigKey("apiKey")?.ToString() ?? "",
        "user" when user.Id != "system-admin" => user.ApiKey,
        _ => ""
    };

    private static string Fingerprint(string source, string userId, string apiKey, string? nodeId = null) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(IConfigBase.JwtSecret),
            Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new[]
                { nodeId == null ? "MSLX.ApiKey.Caller.v1" : "MSLX.ApiKey.Node.v1", source, userId, nodeId, apiKey }))));

    private static bool Matches(string? proof, string expected)
    {
        if (proof == null || proof.Length != expected.Length) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(proof), Encoding.ASCII.GetBytes(expected));
    }
}
