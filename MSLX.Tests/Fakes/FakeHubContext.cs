using Microsoft.AspNetCore.SignalR;

namespace MSLX.Tests.Fakes;

/// <summary>记录所有 SendAsync 调用的假客户端代理</summary>
public class FakeClientProxy : IClientProxy
{
    public List<(string Method, object?[] Args)> Sent { get; } = new();

    public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
    {
        Sent.Add((method, args));
        return Task.CompletedTask;
    }
}

/// <summary>所有通道都路由到同一个记录代理的假 IHubClients</summary>
public class FakeHubClients : IHubClients
{
    public FakeClientProxy GroupProxy { get; } = new();

    public IClientProxy All => GroupProxy;
    public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => GroupProxy;
    public IClientProxy Caller => GroupProxy;
    public IClientProxy Client(string connectionId) => GroupProxy;
    public IClientProxy Clients(IReadOnlyList<string> connectionIds) => GroupProxy;
    public IClientProxy Group(string groupName) => GroupProxy;
    public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => GroupProxy;
    public IClientProxy Groups(IReadOnlyList<string> groupNames) => GroupProxy;
    public IClientProxy Others => GroupProxy;
    public IClientProxy OthersInGroup(string groupName) => GroupProxy;
    public IClientProxy User(string userId) => GroupProxy;
    public IClientProxy Users(IReadOnlyList<string> userIds) => GroupProxy;
}

public class FakeHubContext<T> : IHubContext<T> where T : Hub
{
    public FakeHubClients HubClients { get; } = new();

    public IHubClients Clients => HubClients;

    public IGroupManager Groups => throw new NotImplementedException();
}
