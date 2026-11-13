using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using MSLX.Daemon.Services.NodeServices;
using MSLX.Daemon.Services.PluginsService;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.Models.Node;
using Newtonsoft.Json.Linq;

namespace MSLX.Tests;

[Collection("GlobalConfig")]
public class NodeModeTests : IDisposable
{
    private readonly JObject _config;
    private readonly NodeListConfig _previousNodes;
    private readonly string _nodesPath;
    private readonly string? _nodesFile;
    private readonly MasterNodesConfig _previousMasters;
    private readonly string _mastersPath;
    private readonly string? _mastersFile;

    public NodeModeTests()
    {
        IConfigBase.Initialize(NullLoggerFactory.Instance);
        _config = IConfigBase.Config.ReadConfig();
        _previousNodes = IConfigBase.NodeList;
        _nodesPath = Path.Combine(IConfigBase.GetAppConfigPath(), "Nodes.json");
        _nodesFile = File.Exists(_nodesPath) ? File.ReadAllText(_nodesPath) : null;
        File.WriteAllText(_nodesPath, "[]");
        IConfigBase.NodeList = new NodeListConfig();
        _previousMasters = IConfigBase.MasterNodes;
        _mastersPath = Path.Combine(IConfigBase.GetAppConfigPath(), "Masters.json");
        _mastersFile = File.Exists(_mastersPath) ? File.ReadAllText(_mastersPath) : null;
        File.WriteAllText(_mastersPath, "[]");
        IConfigBase.MasterNodes = new MasterNodesConfig();
        IConfigBase.Config.WriteConfigKey("IsSlaveMode", false);
        IConfigBase.Config.WriteConfigKey("SlaveLinkKey", "");
        NodeModeService.ConfigureStartup(null, null);
    }

    [Fact]
    public void SwitchingCancelsOldAuthorizationEvenAfterReturningToSlaveMode()
    {
        NodeModeService.UpdateSettings(true, null, _ => { });
        Assert.False(string.IsNullOrEmpty(IConfigBase.Config.ReadConfigKey("SlaveLinkKey")?.ToString()));
        var old = NodeModeService.CaptureAuthorization();
        bool disconnected = false;
        using var registration = old.Register(() => disconnected = true);
        NodeModeService.UpdateSettings(false, null, _ => { });
        Assert.True(disconnected);
        Assert.Equal(HttpStatusCode.Unauthorized, Assert.Throws<HttpRequestException>(() => NodeModeService.CaptureAuthorization()).StatusCode);
        NodeModeService.UpdateSettings(true, "changed-key", _ => { });
        bool published = false;
        Assert.Throws<HttpRequestException>(() => NodeModeService.PublishAuthorization(old, () => published = true));
        Assert.False(published);
        var current = NodeModeService.CaptureAuthorization();
        NodeModeService.UpdateSettings(null, "rotated-key", _ => { });
        Assert.True(current.IsCancellationRequested);
        Assert.Throws<HttpRequestException>(() => NodeModeService.PublishAuthorization(current, () => true));
    }

    [Fact]
    public void ChangingKeyRevokesAllPairingsAndAuthorizationButUnchangedKeyPreservesThem()
    {
        NodeModeService.UpdateSettings(true, "original-key", _ => { });
        IConfigBase.MasterNodes.AddOrUpdateMaster(new MasterNodeInfo { MasterId = "master-a", NodeId = "node-a", CommsKey = "comms-a" });
        IConfigBase.MasterNodes.AddOrUpdateMaster(new MasterNodeInfo { MasterId = "master-b", NodeId = "node-b", CommsKey = "comms-b" });
        var lifetime = NodeModeService.CaptureAuthorization();
        NodeModeService.UpdateSettings(null, "original-key", _ => { });
        Assert.Equal(2, IConfigBase.MasterNodes.GetAllMasters().Count);
        Assert.False(lifetime.IsCancellationRequested);
        NodeModeService.UpdateSettings(null, "new-key", _ => { });
        Assert.True(lifetime.IsCancellationRequested);
        Assert.Empty(IConfigBase.MasterNodes.GetAllMasters());
        using var reloaded = new MasterNodesConfig();
        Assert.Empty(reloaded.GetAllMasters());
        Assert.False(IConfigBase.MasterNodes.UpdateRelationship(new UpdateLinkRequest { NodeId = "node-a", CommsKey = "comms-a", MasterUrl = "http://old-master" }));
        Assert.Throws<HttpRequestException>(() => NodeModeService.PublishAuthorization(lifetime, () => true));
    }

    [Fact]
    public void StartupKeyChangeAlsoRevokesStoredPairings()
    {
        NodeModeService.UpdateSettings(true, "original-key", _ => { });
        IConfigBase.MasterNodes.AddOrUpdateMaster(new MasterNodeInfo { MasterId = "master-a", NodeId = "node-a", CommsKey = "comms-a" });
        NodeModeService.ConfigureStartup(true, "original-key");
        using (var same = new MasterNodesConfig()) Assert.Single(same.GetAllMasters());
        NodeModeService.ConfigureStartup(true, "replacement-key");
        using var changed = new MasterNodesConfig();
        Assert.Empty(changed.GetAllMasters());
    }

    [Fact]
    public void PairedNodesAndStartupParametersBlockModeChangesBeforeAnySettingIsWritten()
    {
        var node = new SlaveNodeConfig { NodeId = "mode-test" };
        IConfigBase.NodeList.AddOrUpdateNode(node);
        Assert.Throws<SyncConflictException>(() => NodeModeService.UpdateSettings(true, null, c => c["downloadThreadCount"] = 7));
        Assert.False(NodeModeService.IsSlaveMode);
        Assert.Equal("", IConfigBase.Config.ReadConfigKey("SlaveLinkKey")?.Value<string>());
        Assert.True(NodeModeService.ConfigureStartup(true, "cli-secret"));
        Assert.True(NodeModeService.ModeLocked);
        Assert.True(NodeModeService.LinkKeyLocked);
        Assert.False(NodeModeService.IsSlaveMode);
        Assert.Throws<SyncConflictException>(() => NodeModeService.UpdateSettings(true, null, _ => { }));
        Assert.Throws<SyncConflictException>(() => NodeModeService.UpdateSettings(null, "other", _ => { }));
        NodeModeService.UpdateSettings(null, null, _ => { }); // Old callers preserve mode and key.
        Assert.Equal("cli-secret", IConfigBase.Config.ReadConfigKey("SlaveLinkKey")!.Value<string>());
    }

    [Fact]
    public void FailedPairingRemovalDoesNotChangeKeyOrDisconnect()
    {
        NodeModeService.UpdateSettings(true, "saved-key", _ => { });
        IConfigBase.MasterNodes.AddOrUpdateMaster(new MasterNodeInfo { MasterId = "master-a", NodeId = "node-a", CommsKey = "comms-a" });
        var lifetime = NodeModeService.CaptureAuthorization();
        var original = File.ReadAllText(_mastersPath);
        try
        {
            File.Delete(_mastersPath);
            Directory.CreateDirectory(_mastersPath);
            Assert.ThrowsAny<IOException>(() => NodeModeService.UpdateSettings(null, "failed-key", _ => { }));
            Assert.Equal("saved-key", IConfigBase.Config.ReadConfigKey("SlaveLinkKey")!.Value<string>());
            Assert.False(lifetime.IsCancellationRequested);
            Assert.Single(IConfigBase.MasterNodes.GetAllMasters());
        }
        finally
        {
            Directory.Delete(_mastersPath);
            File.WriteAllText(_mastersPath, original);
        }
    }

    [Fact]
    public void FailedAtomicSaveDoesNotPublishNewModeOrCancelExistingAuthorization()
    {
        NodeModeService.UpdateSettings(true, "saved-key", _ => { });
        IConfigBase.MasterNodes.AddOrUpdateMaster(new MasterNodeInfo { MasterId = "master-a", NodeId = "node-a", CommsKey = "comms-a" });
        var lifetime = NodeModeService.CaptureAuthorization();
        var path = Path.Combine(IConfigBase.GetAppConfigPath(), "Config.json");
        var original = File.ReadAllText(path);
        try
        {
            File.Delete(path);
            Directory.CreateDirectory(path);
            Assert.ThrowsAny<IOException>(() => NodeModeService.UpdateSettings(false, null, _ => { }));
            Assert.ThrowsAny<IOException>(() => NodeModeService.UpdateSettings(null, "failed-key", _ => { }));
            Assert.True(NodeModeService.IsSlaveMode);
            Assert.False(lifetime.IsCancellationRequested);
            Assert.Equal("saved-key", IConfigBase.Config.ReadConfigKey("SlaveLinkKey")!.Value<string>());
            Assert.Single(IConfigBase.MasterNodes.GetAllMasters());
            using var reloaded = new MasterNodesConfig();
            Assert.Single(reloaded.GetAllMasters());
        }
        finally
        {
            Directory.Delete(path);
            File.WriteAllText(path, original);
        }
    }

    public void Dispose()
    {
        NodeModeService.ConfigureStartup(null, null);
        IConfigBase.Config.WriteConfig(_config);
        IConfigBase.NodeList.Dispose();
        IConfigBase.NodeList = _previousNodes;
        if (_nodesFile == null) File.Delete(_nodesPath); else File.WriteAllText(_nodesPath, _nodesFile);
        IConfigBase.MasterNodes.Dispose();
        IConfigBase.MasterNodes = _previousMasters;
        if (_mastersFile == null) File.Delete(_mastersPath); else File.WriteAllText(_mastersPath, _mastersFile);
    }
}
