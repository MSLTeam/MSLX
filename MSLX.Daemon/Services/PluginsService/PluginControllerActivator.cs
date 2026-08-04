using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace MSLX.Daemon.Services.PluginsService;

public class PluginControllerActivator : IControllerActivator
{
    private readonly PluginManager _pluginManager;

    public PluginControllerActivator(PluginManager pluginManager)
    {
        _pluginManager = pluginManager;
    }

    public object Create(ControllerContext context)
    {
        var type = context.ActionDescriptor.ControllerTypeInfo.AsType();
        if (_pluginManager.PluginProviders.TryGetValue(type.Assembly, out var provider))
        {
            return ActivatorUtilities.CreateInstance(provider, type);
        }

        return ActivatorUtilities.CreateInstance(context.HttpContext.RequestServices, type);
    }

    public void Release(ControllerContext context, object controller)
    {
        if (controller is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
