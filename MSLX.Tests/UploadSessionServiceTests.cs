using System.Security.Claims;
using Microsoft.Extensions.Caching.Memory;
using MSLX.Daemon.Services;

namespace MSLX.Tests;

public sealed class UploadSessionServiceTests : IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly UploadSessionService _service;

    public UploadSessionServiceTests()
    {
        _service = new UploadSessionService(_cache);
    }

    public void Dispose() => _cache.Dispose();

    [Fact]
    public void SessionIsAccessibleOnlyToItsOwner()
    {
        var owner = Principal("user-a");
        var other = Principal("user-b");
        const string uploadId = "0123456789abcdef0123456789abcdef";

        _service.Create(uploadId, owner, "node-1");

        Assert.True(_service.IsOwned(uploadId, owner));
        Assert.False(_service.IsOwned(uploadId, other));
        Assert.True(_service.TryGet(uploadId, out var session));
        Assert.Equal("user-a", session!.OwnerId);
        Assert.Equal("node-1", session.NodeId);
    }

    [Fact]
    public void RemovingSessionRevokesAccess()
    {
        const string uploadId = "fedcba9876543210fedcba9876543210";
        var owner = Principal("user-a");

        _service.Create(uploadId, owner, null);
        _service.Remove(uploadId);

        Assert.False(_service.IsOwned(uploadId, owner));
        Assert.False(_service.TryGet(uploadId, out _));
    }

    private static ClaimsPrincipal Principal(string userId) => new(new ClaimsIdentity(
        new[] { new Claim("UserId", userId) }, "test"));
}
