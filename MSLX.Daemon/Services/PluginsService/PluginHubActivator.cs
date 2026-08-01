using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace MSLX.Daemon.Services.PluginsService;

public class PluginHubActivator<THub> : IHubActivator<THub> where THub : Hub
{
    private readonly PluginManager _pluginManager;
    private readonly IServiceProvider _rootServiceProvider;

    public PluginHubActivator(PluginManager pluginManager, IServiceProvider rootServiceProvider)
    {
        _pluginManager = pluginManager;
        _rootServiceProvider = rootServiceProvider;
    }

    public THub Create()
    {
        var type = typeof(THub);
        var plugin = _pluginManager.Plugins.FirstOrDefault(p => p.Assembly == type.Assembly);

        if (plugin != null && plugin.ServiceProvider != null)
        {
            return (THub)ActivatorUtilities.CreateInstance(plugin.ServiceProvider, type);
        }

        return (THub)ActivatorUtilities.CreateInstance(_rootServiceProvider, type);
    }

    public void Release(THub hub)
    {
        if (hub is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
