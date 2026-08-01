using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using System.IO;
using System.Linq;

namespace MSLX.Daemon.Services.PluginsService;

public class PluginDynamicFileProvider : IFileProvider
{
    private readonly PluginManager _pluginManager;

    public PluginDynamicFileProvider(PluginManager pluginManager)
    {
        _pluginManager = pluginManager;
    }

    public IDirectoryContents GetDirectoryContents(string subpath)
    {
        return new NotFoundDirectoryContents();
    }

    public IFileInfo GetFileInfo(string subpath)
    {
        // 类似 "/mslx-plugin-xxx/1.0.0/mslx-plugin-entry.js"
        var pathParts = subpath.TrimStart('/').Split('/', 3);
        if (pathParts.Length == 3)
        {
            var pluginId = pathParts[0];
            var pluginVersion = pathParts[1];
            var filePath = "/" + pathParts[2];

            var plugin = _pluginManager.Plugins.FirstOrDefault(p => 
                p.Metadata.Id.Equals(pluginId, StringComparison.OrdinalIgnoreCase) && 
                p.Metadata.Version == pluginVersion);

            if (plugin != null)
            {
                try
                {
                    var provider = new ManifestEmbeddedFileProvider(plugin.Assembly, "Frontend/dist");
                    var fileInfo = provider.GetFileInfo(filePath);
                    if (fileInfo.Exists)
                    {
                        return fileInfo;
                    }
                }
                catch
                {
                    // 忽略没有静态文件
                }
            }
        }
        
        return new NotFoundFileInfo(subpath);
    }

    public IChangeToken Watch(string filter)
    {
        return NullChangeToken.Singleton;
    }
}
