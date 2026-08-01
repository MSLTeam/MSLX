using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using MSLX.SDK;
using MSLX.Daemon.Utils.ConfigUtils;

namespace MSLX.Daemon.Services.PluginsService;

public class PluginManager
{
    public List<LoadedPlugin> Plugins { get; } = new();

    private IServiceProvider? _serviceProvider;
    private ApplicationPartManager? _partManager;
    private ILogger<PluginManager>? _logger;
    private IServiceCollection? _rootServiceCollection;

    public void Initialize(IServiceProvider serviceProvider, ApplicationPartManager partManager, ILogger<PluginManager> logger, IServiceCollection rootServiceCollection)
    {
        _serviceProvider = serviceProvider;
        _partManager = partManager;
        _logger = logger;
        _rootServiceCollection = rootServiceCollection;
    }

    public IPlugin? GetPluginMetadata(string dllPath)
    {
        try
        {
            var loadContext = new PluginLoadContext(dllPath);
            using var fs = new FileStream(dllPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var assembly = loadContext.LoadFromStream(fs);
            var pluginType = assembly.GetTypes().FirstOrDefault(t => 
                typeof(IPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);
            
            if (pluginType == null)
            {
                loadContext.Unload();
                return null;
            }

            var pluginInstance = (IPlugin)Activator.CreateInstance(pluginType)!;
            // 卸载 ALC。当 pluginInstance 被 GC 回收后，ALC 及其程序集也会被物理卸载。
            loadContext.Unload(); 
            return pluginInstance;
        }
        catch
        {
            return null;
        }
    }

    public bool LoadPlugin(string dllPath)
    {
        if (_serviceProvider == null || _partManager == null || _logger == null)
            return false;

        try
        {
            var disabledMarker = dllPath + ".disabled";
            if (File.Exists(disabledMarker))
            {
                _logger.LogInformation($"[MSLX Plugin] 插件被禁用，跳过加载: {Path.GetFileName(dllPath)}");
                return false;
            }

            // 1. 初始化独立的 AssemblyLoadContext
            var loadContext = new PluginLoadContext(dllPath);
            
            // 使用 FileStream 读取 DLL，避免文件被独占锁定，使得后续可以直接覆盖下载更新
            using var fs = new FileStream(dllPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var pdbPath = Path.ChangeExtension(dllPath, ".pdb");
            Assembly assembly;
            
            if (File.Exists(pdbPath))
            {
                using var pdbStream = new FileStream(pdbPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                assembly = loadContext.LoadFromStream(fs, pdbStream);
            }
            else
            {
                assembly = loadContext.LoadFromStream(fs);
            }

            var pluginType = assembly.GetTypes().FirstOrDefault(t => 
                typeof(IPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

            if (pluginType == null)
            {
                _logger.LogWarning($"[MSLX Plugin] 未找到实现 IPlugin 的类: {dllPath}");
                loadContext.Unload();
                return false;
            }

            var pluginInstance = (IPlugin)Activator.CreateInstance(pluginType)!;
            
            // 校验兼容性
            var hostVersion = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version("0.0.0.0");
            if (Version.TryParse(pluginInstance.MinSDKVersion?.TrimStart('v', 'V'), out var minVersion) && minVersion > hostVersion)
            {
                _logger.LogWarning($"[MSLX Plugin] [兼容性警告] 插件 '{pluginInstance.Name}' 要求最低节点版本 v{minVersion}，当前 v{hostVersion}");
            }

            // 构建插件专属的 ServiceCollection
            var pluginServices = new ServiceCollection();
            
            // 将主程序的 Service 映射给插件
            if (_rootServiceCollection != null && _serviceProvider != null)
            {
                foreach (var descriptor in _rootServiceCollection)
                {
                    if (descriptor.Lifetime == ServiceLifetime.Singleton && !descriptor.ServiceType.IsGenericTypeDefinition)
                    {
                        // 单例直接从 Root 获取，保证全局状态一致
                        pluginServices.AddSingleton(descriptor.ServiceType, sp => _serviceProvider.GetService(descriptor.ServiceType)!);
                    }
                    else
                    {
                        ((IServiceCollection)pluginServices).Add(descriptor);
                    }
                }
            }

            // 让插件注册自己的服务
            pluginInstance.OnRegisterServices(pluginServices);
            
            // 构建插件的 Provider 并生成 Scope
            var pluginProvider = pluginServices.BuildServiceProvider();
            var scope = pluginProvider.CreateScope();

            pluginInstance.OnPluginInitialize(scope.ServiceProvider);
            pluginInstance.OnLoad();

            // 挂载路由
            var part = new AssemblyPart(assembly);
            _partManager.ApplicationParts.Add(part);
            NotifyRouteChanges();

            Plugins.Add(new LoadedPlugin 
            { 
                Assembly = assembly, 
                Metadata = pluginInstance,
                LoadContext = loadContext,
                ServiceProvider = pluginProvider,
                Scope = scope,
                Part = part,
                DllPath = dllPath
            });

            _logger.LogInformation($"[MSLX Plugin] 成功加载插件: {pluginInstance.Name} v{pluginInstance.Version}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"[MSLX Plugin] 插件加载失败 ({Path.GetFileName(dllPath)}): {ex.Message}");
            return false;
        }
    }

    public bool UnloadPlugin(string dllPath)
    {
        if (_partManager == null || _logger == null) return false;

        var plugin = Plugins.FirstOrDefault(p => p.DllPath.Equals(dllPath, StringComparison.OrdinalIgnoreCase));
        if (plugin == null) return false;

        try
        {
            _logger.LogInformation($"[MSLX Plugin] 正在卸载插件: {plugin.Metadata.Name}");

            // 1. 调用生命周期
            plugin.Metadata.OnUnload();

            // 2. 移除路由并刷新
            if (plugin.Part != null)
            {
                _partManager.ApplicationParts.Remove(plugin.Part);
                NotifyRouteChanges();
            }

            // 3. 释放 DI Scope
            plugin.Scope?.Dispose();
            if (plugin.ServiceProvider is IDisposable spDisposable)
            {
                spDisposable.Dispose();
            }

            // 4. 从列表中移除
            Plugins.Remove(plugin);

            // 5. 卸载上下文 (异步回收)
            plugin.LoadContext?.Unload();

            // 手动触发 GC 以尝试立刻回收
            GC.Collect();
            GC.WaitForPendingFinalizers();
            
            _logger.LogInformation($"[MSLX Plugin] 插件已卸载: {plugin.Metadata.Name}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"[MSLX Plugin] 插件卸载失败: {ex.Message}");
            return false;
        }
    }

    private void NotifyRouteChanges()
    {
        HotReloadActionDescriptorChangeProvider.Instance.HasChanged = true;
        HotReloadActionDescriptorChangeProvider.Instance.TokenSource?.Cancel();
    }
}

public class HotReloadActionDescriptorChangeProvider : IActionDescriptorChangeProvider
{
    public static HotReloadActionDescriptorChangeProvider Instance { get; } = new HotReloadActionDescriptorChangeProvider();
    
    public Microsoft.Extensions.Primitives.IChangeToken GetChangeToken()
    {
        TokenSource = new CancellationTokenSource();
        return new Microsoft.Extensions.Primitives.CancellationChangeToken(TokenSource.Token);
    }
    
    public CancellationTokenSource? TokenSource { get; private set; }
    public bool HasChanged { get; set; }
}

public class LoadedPlugin
{
    public Assembly Assembly { get; set; } = null!;
    public IPlugin Metadata { get; set; } = null!;
    public PluginLoadContext LoadContext { get; set; } = null!;
    public IServiceProvider ServiceProvider { get; set; } = null!;
    public IServiceScope Scope { get; set; } = null!;
    public ApplicationPart Part { get; set; } = null!;
    public string DllPath { get; set; } = string.Empty;
}
