namespace MSLX.Daemon.Services;

public class PluginManager
{
    public List<LoadedPlugin> Plugins { get; } = new();
}

public class LoadedPlugin
{
    public System.Reflection.Assembly Assembly { get; set; } = null!;
    public MSLX.SDK.IPlugin Metadata { get; set; } = null!;
}
