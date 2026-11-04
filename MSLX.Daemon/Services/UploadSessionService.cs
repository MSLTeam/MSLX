using System.Security.Claims;
using Microsoft.Extensions.Caching.Memory;

namespace MSLX.Daemon.Services;

public sealed record UploadSessionInfo(string OwnerId, string NodeId, DateTimeOffset CreatedAt);

/// <summary>
/// 维护上传会话的服务端归属，避免仅凭 uploadId 访问其他用户的临时文件。
/// </summary>
public sealed class UploadSessionService(IMemoryCache cache)
{
    private const string SessionPrefix = "Upload_Session_";
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(2);

    public static string GetCacheKey(string uploadId) => SessionPrefix + uploadId;

    public string ResolveOwnerId(ClaimsPrincipal? principal)
    {
        return principal?.FindFirst("UserId")?.Value
            ?? principal?.Identity?.Name
            ?? string.Empty;
    }

    public void Create(string uploadId, ClaimsPrincipal principal, string? nodeId)
    {
        var ownerId = ResolveOwnerId(principal);
        if (string.IsNullOrWhiteSpace(ownerId))
            throw new InvalidOperationException("无法确定上传会话所属用户");

        Create(uploadId, ownerId, nodeId);
    }

    public void Create(string uploadId, string ownerId, string? nodeId)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
            throw new ArgumentException("上传会话所属用户不能为空", nameof(ownerId));

        cache.Set(GetCacheKey(uploadId), new UploadSessionInfo(
            ownerId,
            nodeId ?? "local",
            DateTimeOffset.UtcNow), SessionLifetime);
    }

    public bool TryGet(string? uploadId, out UploadSessionInfo? session)
    {
        if (string.IsNullOrWhiteSpace(uploadId))
        {
            session = null;
            return false;
        }

        return cache.TryGetValue(GetCacheKey(uploadId), out session) && session != null;
    }

    public bool IsOwned(string? uploadId, string? ownerId)
    {
        return !string.IsNullOrWhiteSpace(ownerId)
            && TryGet(uploadId, out var session)
            && string.Equals(session!.OwnerId, ownerId, StringComparison.Ordinal);
    }

    public bool IsOwned(string uploadId, ClaimsPrincipal? principal)
    {
        return IsOwned(uploadId, ResolveOwnerId(principal));
    }

    public void Remove(string uploadId)
    {
        cache.Remove(GetCacheKey(uploadId));
    }
}
