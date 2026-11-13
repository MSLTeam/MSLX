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

    public NodeModeTests()
    {
        IConfigBase.Initialize(NullLoggerFactory.Instance);
        _config = IConfigBase.Config.ReadConfig();
        _previousNodes = IConfigBase.NodeList;
        _nodesPath = Path.Combine(IConfigBase.GetAppConfigPath(), "Nodes.json");
        _nodesFile = File.Exists(_nodesPath) ? File.ReadAllText(_nodesPath) : null;
        File.WriteAllText(_nodesPath, "[]");
        IConfigBase.NodeList = new NodeListConfig();
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
        Assert.False(current.IsCancellationRequested);
        Assert.True(NodeModeService.PublishAuthorization(current, () => true));
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
    public void FailedAtomicSaveDoesNotPublishNewModeOrCancelExistingAuthorization()
    {
        NodeModeService.UpdateSettings(true, "saved-key", _ => { });
        var lifetime = NodeModeService.CaptureAuthorization();
        var path = Path.Combine(IConfigBase.GetAppConfigPath(), "Config.json");
        var original = File.ReadAllText(path);
        try
        {
            File.Delete(path);
            Directory.CreateDirectory(path);
            Assert.ThrowsAny<IOException>(() => NodeModeService.UpdateSettings(false, null, _ => { }));
            Assert.True(NodeModeService.IsSlaveMode);
            Assert.False(lifetime.IsCancellationRequested);
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
    }
}
