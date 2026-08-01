using System.Reflection;
using System.Runtime.Loader;

namespace MSLX.Daemon.Services.PluginsService;

public class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string pluginPath) : base(isCollectible: true)
    {
        _resolver = new AssemblyDependencyResolver(pluginPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name == "MSLX.SDK" || 
            assemblyName.Name?.StartsWith("Microsoft.AspNetCore") == true ||
            assemblyName.Name?.StartsWith("Microsoft.Extensions") == true ||
            assemblyName.Name?.StartsWith("System.") == true)
        {
            return null;
        }

        string? assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath != null)
        {
            return LoadFromAssemblyPath(assemblyPath);
        }

        return null;
    }
}
