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

    public IEndpointRouteBuilder? AppRouteBuilder { get; private set; }
    public PluginCompositeEndpointDataSource DynamicEndpoints { get; } = new();

    // 弱引用表保存 Assembly 到 ServiceProvider 的映射，避免卸载时因未断开的连接导致 DI 解析失败崩溃
    public System.Runtime.CompilerServices.ConditionalWeakTable<Assembly, IServiceProvider> PluginProviders { get; } = new();

    public void Initialize(IServiceProvider serviceProvider, ApplicationPartManager partManager, ILogger<PluginManager> logger, IServiceCollection rootServiceCollection, Microsoft.AspNetCore.Routing.IEndpointRouteBuilder app)
    {
        _serviceProvider = serviceProvider;
        _partManager = partManager;
        _logger = logger;
        _rootServiceCollection = rootServiceCollection;
        AppRouteBuilder = app;
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
            // 卸载 ALC
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

            // 初始化独立的 AssemblyLoadContext
            var loadContext = new PluginLoadContext(dllPath);
            
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

            // 插件专属 ServiceCollection
            var pluginServices = new ServiceCollection();
            
            // 将主程序的 Service 映射给插件
            if (_rootServiceCollection != null && _serviceProvider != null)
            {
                foreach (var descriptor in _rootServiceCollection)
                {
                    if (descriptor.Lifetime == ServiceLifetime.Singleton && !descriptor.ServiceType.IsGenericTypeDefinition)
                    {
                        pluginServices.AddSingleton(descriptor.ServiceType, sp => _serviceProvider.GetService(descriptor.ServiceType)!);
                    }
                    else
                    {
                        ((IServiceCollection)pluginServices).Add(descriptor);
                    }
                }
            }

            // 修正依赖注入 DI 问题：拦截插件内部的 Hub，确保其 IHubContext<T> 使用 Root Provider 的单例
            try
            {
                var hubTypes = assembly.GetTypes().Where(t => typeof(Microsoft.AspNetCore.SignalR.Hub).IsAssignableFrom(t) && !t.IsAbstract);
                foreach (var hubType in hubTypes)
                {
                    var hubContextType = typeof(Microsoft.AspNetCore.SignalR.IHubContext<>).MakeGenericType(hubType);
                    if (_serviceProvider != null)
                    {
                        pluginServices.AddSingleton(hubContextType, sp => _serviceProvider.GetService(hubContextType)!);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[MSLX Plugin] 注册 HubContext 失败: {ex.Message}");
            }

            // 插件注册自己的服务
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

            List<Microsoft.AspNetCore.Routing.EndpointDataSource> capturedDataSources = new();
            if (AppRouteBuilder != null)
            {
                var routeWrapper = new PluginEndpointRouteBuilderWrapper(AppRouteBuilder);
                try
                {
                    pluginInstance.OnRegisterEndpoints(routeWrapper);
                    if (routeWrapper.CapturedDataSources.Count > 0)
                    {
                        DynamicEndpoints.AddDataSources(routeWrapper.CapturedDataSources);
                        capturedDataSources = routeWrapper.CapturedDataSources;
                        _logger.LogInformation($"[MSLX Plugin] 插件高级路由端点动态注册成功: {pluginInstance.Name}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[MSLX Plugin] 插件 {pluginInstance.Name} 注册扩展路由时抛出异常: {ex.Message}");
                }
            }

            // 注册到弱引用表
            PluginProviders.AddOrUpdate(assembly, pluginProvider);

            Plugins.Add(new LoadedPlugin 
            { 
                Assembly = assembly, 
                Metadata = pluginInstance,
                LoadContext = loadContext,
                ServiceProvider = pluginProvider,
                Scope = scope,
                Part = part,
                DllPath = dllPath,
                DataSourcesAdded = capturedDataSources
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

            // 调用生命周期
            plugin.Metadata.OnUnload();
            
            // 尝试断开插件的所有SignalR连接
            try
            {
                var hubTypes = plugin.Assembly.GetTypes().Where(t => typeof(Microsoft.AspNetCore.SignalR.Hub).IsAssignableFrom(t) && !t.IsAbstract);
                foreach (var hubType in hubTypes)
                {
                    var hubContextType = typeof(Microsoft.AspNetCore.SignalR.IHubContext<>).MakeGenericType(hubType);
                    if (_serviceProvider != null)
                    {
                        var hubContext = _serviceProvider.GetService(hubContextType);
                        if (hubContext != null)
                        {
                            var clientsProp = hubContextType.GetProperty("Clients");
                            if (clientsProp != null)
                            {
                                var clients = clientsProp.GetValue(hubContext);
                                var allProp = clients?.GetType().GetProperty("All");
                                if (allProp != null)
                                {
                                    var all = allProp.GetValue(clients);
                                    var sendAsyncMethod = typeof(Microsoft.AspNetCore.SignalR.ClientProxyExtensions)
                                        .GetMethod("SendCoreAsync", new[] { typeof(Microsoft.AspNetCore.SignalR.IClientProxy), typeof(string), typeof(object[]), typeof(CancellationToken) });
                                    
                                    if (sendAsyncMethod != null && all != null)
                                    {
                                        // 发送 ForcePluginReload 消息
                                        sendAsyncMethod.Invoke(null, new object[] { all, "ForcePluginReload", Array.Empty<object>(), CancellationToken.None });
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"[MSLX Plugin] 卸载时通知 SignalR 客户端失败: {ex.Message}");
            }

            // 移除路由并刷新
            if (plugin.Part != null)
            {
                _partManager.ApplicationParts.Remove(plugin.Part);
                
                // 动态移除该插件注册的所有路由端点
                if (DynamicEndpoints != null)
                {
                    DynamicEndpoints.RemoveDataSources(plugin.DataSourcesAdded);
                }
                NotifyRouteChanges();
            }

            // 释放 DI Scope (不强制 Dispose Provider，依靠弱引用表和 GC 自动回收，防止僵尸连接崩溃)
            plugin.Scope?.Dispose();

            // 从列表中移除
            Plugins.Remove(plugin);

            // 卸载上下文
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
    public List<Microsoft.AspNetCore.Routing.EndpointDataSource> DataSourcesAdded { get; set; } = new();
}
