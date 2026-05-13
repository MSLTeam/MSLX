namespace MSLX.SDK;

public static class PluginExtensions
{
    public static Interfaces.IPluginConfigBridge Config(this IPlugin plugin)
    {
        return MSLX.Config.GetPluginConfig(plugin.Id);
    }
}