namespace MSLX.SDK.Models;

public class PluginInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Developer { get; set; } = "";
    public string EntryPath => $"/plugins/{Id.ToLower()}/{Version.ToLower()}/mslx-plugin-entry.js"; 
}