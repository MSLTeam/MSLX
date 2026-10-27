using Newtonsoft.Json.Linq;

namespace MSLX.SDK.Models.Node;

public class SettingsSyncRequest
{
    public JObject Values { get; set; } = new();
    public string ExpectedRevision { get; set; } = "";
}

public class NodeSyncRequest
{
    public List<string> TargetNodeIds { get; set; } = [];
    public List<string> SettingKeys { get; set; } = [];
    public List<string> PluginIds { get; set; } = [];
    public bool IncludePluginConfig { get; set; }
    public string SourceRevision { get; set; } = "";
    public Dictionary<string, string> TargetRevisions { get; set; } = new();
}

public class NodeSyncItemResult
{
    public string NodeId { get; set; } = "";
    public string Item { get; set; } = "";
    public string Status { get; set; } = "pending";
    public string Message { get; set; } = "";
}
