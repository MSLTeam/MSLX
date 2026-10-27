using Microsoft.Extensions.Logging.Abstractions;
using MSLX.Daemon.Controllers.NodesControllers;
using MSLX.Daemon.Services;
using MSLX.Daemon.Services.InstanceServices;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.Models.Node;
using Microsoft.AspNetCore.Mvc;

namespace MSLX.Tests;

[Collection("GlobalConfig")]
public class NodeLifecycleTests
{
    [Fact]
    public void NewPairingRotatesIdentity_RetryRecoversSameIdentity_StaleCleanupCannotDeleteIt()
    {
        IConfigBase.Initialize(NullLoggerFactory.Instance);
        var path = Path.Combine(IConfigBase.GetAppConfigPath(), "Masters.json");
        var original = File.Exists(path) ? File.ReadAllText(path) : null;
        using var config = new MasterNodesConfig();
        try
        {
            var request = new AcceptLinkRequest { MasterId = "pairing-test", PairingId = Guid.NewGuid().ToString("N"), MasterUrl = "http://master.test" };
            var first = config.AcceptPairing(request);
            Assert.Equal(first.NodeId, config.AcceptPairing(request).NodeId);
            request.PairingId = Guid.NewGuid().ToString("N");
            var next = config.AcceptPairing(request);
            Assert.NotEqual(first.NodeId, next.NodeId);
            Assert.NotEqual(first.CommsKey, next.CommsKey);
            Assert.False(config.RemoveRelationship(first.NodeId, first.CommsKey));
            Assert.NotNull(config.GetMasterById(next.NodeId));
            Assert.True(config.RemoveRelationship(next.NodeId, next.CommsKey));
        }
        finally { if (original == null) File.Delete(path); else File.WriteAllText(path, original); }
    }

    [Fact]
    public void PendingRemovalCannotBeUndoneByAnInflightEdit_ForceIsExplicitAndIdempotent()
    {
        IConfigBase.Initialize(NullLoggerFactory.Instance);
        var path = Path.Combine(IConfigBase.GetAppConfigPath(), "Nodes.json");
        var original = File.Exists(path) ? File.ReadAllText(path) : null;
        var previous = IConfigBase.NodeList;
        using var config = new NodeListConfig();
        try
        {
            IConfigBase.NodeList = config;
            var node = new SlaveNodeConfig { NodeId = "unlink-test", CommsKey = "old-proof" };
            config.AddOrUpdateNode(node);
            var controller = new MasterNodeController(NullLogger<MasterNodeController>.Instance);
            Assert.IsType<ConflictObjectResult>(controller.UnlinkNode(node.NodeId, true));
            Assert.IsType<OkObjectResult>(controller.UnlinkNode(node.NodeId));
            Assert.False(config.UpdateActiveNode(node));
            Assert.False(config.RemovePendingNode(node.NodeId, "stale-key"));
            Assert.IsType<OkObjectResult>(controller.UnlinkNode(node.NodeId, true));
            Assert.IsType<OkObjectResult>(controller.UnlinkNode(node.NodeId, true));
            Assert.False(config.UpdateActiveNode(node));
        }
        finally { IConfigBase.NodeList = previous; if (original == null) File.Delete(path); else File.WriteAllText(path, original); }
    }

    [Fact]
    public void RemovalRetryBackoffIsBounded_AndDiscardedWhenRecordDisappears()
    {
        var retries = new NodeRemovalRetries();
        var now = DateTimeOffset.UtcNow;
        foreach (var seconds in new[] { 5, 15, 30, 60, 300, 300 })
        {
            Assert.True(retries.IsDue("node", now));
            retries.Failed("node", now);
            Assert.False(retries.IsDue("node", now.AddSeconds(seconds - 1)));
            now = now.AddSeconds(seconds);
        }
        retries.Retain([]);
        Assert.True(retries.IsDue("node", now));
    }

    [Fact]
    public void ConsoleReplayOnlyReturnsMissedOutput_ReportsOverflowAndProcessReplacement()
    {
        var buffer = new ConsoleOutputBuffer(3);
        var first = buffer.Append("a");
        var second = buffer.Append("b");
        buffer.Append("c");
        Assert.Equal(new[] { "b", "c" }, buffer.Replay(first).Frames.Select(f => f.Text));
        Assert.False(buffer.Replay(first).Gap);
        buffer.Append("d"); buffer.Append("e");
        Assert.True(buffer.Replay(first).Gap);
        Assert.False(buffer.Replay(second).Gap);
        Assert.True(new ConsoleOutputBuffer(3).Replay(first).Gap);
    }

    [Fact]
    public void ConcurrentConsoleWritesProduceOneContiguousCursorSequence()
    {
        var buffer = new ConsoleOutputBuffer(1000);
        Parallel.For(0, 1000, i => buffer.Append(i.ToString()));
        Assert.Equal(Enumerable.Range(1, 1000).Select(i => (long)i), buffer.Replay(null).Frames.Select(f => f.Sequence));
    }
}
