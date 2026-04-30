using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using MSLX.Daemon.Hubs;
using MSLX.Daemon.Middleware;
using MSLX.Daemon.Services;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.BackgroundTasks;
using MSLX.Daemon.Utils.ConfigUtils;
using System.Reflection;
using MSLX.Daemon.Services.DeployServerService;
using MSLX.SDK.IServices;
using MSLX.SDK.Models;

System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

var builder = WebApplication.CreateBuilder(args);


// 日志级别配置
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
builder.Logging.AddFilter("System", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);

// 创建临时 Logger
using (var bootstrapLoggerFactory = LoggerFactory.Create(logging => logging.AddConsole()))
{
    IConfigBase.Initialize(bootstrapLoggerFactory);
}

// 检查启动参数
var argHost = builder.Configuration["host"]; // 支持 --host 或 /host
var argPort = builder.Configuration["port"]; // 支持 --port 或 /port
var argNoBrowser = builder.Configuration["nobrowser"]; // 支持 --nobrowser 或 /nobrowser
bool configUpdated = false;

//传入了 host 参数，更新配置
if (!string.IsNullOrWhiteSpace(argHost))
{
    IConfigBase.Config.WriteConfigKey("listenHost", argHost);
    configUpdated = true;
}

// 传入了 port 参数，更新配置
if (!string.IsNullOrWhiteSpace(argPort))
{
    IConfigBase.Config.WriteConfigKey("listenPort", argPort);
    configUpdated = true;
}

if (!string.IsNullOrWhiteSpace(argNoBrowser))
{
    if (argNoBrowser == "true")
    {
        IConfigBase.Config.WriteConfigKey("openWebConsoleOnLaunch", !bool.Parse(argNoBrowser));
        configUpdated = true;
    }
}

if (configUpdated)
{
    var loggerTemp = LoggerFactory.Create(l => l.AddConsole()).CreateLogger("Bootstrap");
    loggerTemp.LogInformation($"检测到启动参数，配置已更新为 Host: {argHost}, Port: {argPort}");
}

// 读取最终配置
string finalIp = IConfigBase.Config.ReadConfig()["listenHost"]?.ToString() ?? "";
string finalPort = IConfigBase.Config.ReadConfig()["listenPort"]?.ToString() ?? "";

// 默认值回退
string targetIp = string.IsNullOrEmpty(finalIp) ? "localhost" : finalIp;
string targetPort = string.IsNullOrWhiteSpace(finalPort) ? "1027" : finalPort;

string listenAddr = $"http://{targetIp}:{targetPort}";

// 应用监听地址
builder.WebHost.UseUrls(listenAddr);

builder.Services.AddControllers().AddNewtonsoftJson();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 跨域请求配置
builder.Services.AddCors(options =>
{
    options.AddPolicy(name: "AllowAll",
        policy  =>
        {
            policy.AllowAnyOrigin()
                .AllowAnyHeader()
                .AllowAnyMethod();
        });
});

builder.Services.AddSignalR();

// 权限拦截处理器
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler, CustomAuthorizationResultHandler>();

// 注册单例服务
builder.Services.AddSingleton<IFrpProcessService, FrpProcessService>();
builder.Services.AddSingleton(typeof(IBackgroundTaskQueue<>), typeof(BackgroundTaskQueue<>));
builder.Services.AddSingleton<IMCServerService,MCServerService>();
builder.Services.AddSingleton<SystemMonitor>();
var pluginManager = new PluginManager();
builder.Services.AddSingleton(pluginManager);

// 后台服务注册
builder.Services.AddHostedService<ServerCreationService>();
builder.Services.AddHostedService<ServerUpdateService>();
builder.Services.AddHostedService<TempFileCleanupService>();
builder.Services.AddHostedService<TaskSchedulerService>();
builder.Services.AddHostedService<SystemMonitorWorker>();

// 瞬时服务注册
builder.Services.AddScoped<IJavaScannerService,JavaScannerService>();
builder.Services.AddTransient<NeoForgeInstallerService>();
builder.Services.AddTransient<ServerDeploymentService>();

// 配置真实IP回传协议
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear(); 
    options.KnownProxies.Clear();
});

// 错误中间件
var mvcBuilder = builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var firstErrorMessage = context.ModelState.Values
                .SelectMany(v => v.Errors)
                .FirstOrDefault()?.ErrorMessage ?? "请求参数验证失败";
            var response = new ApiResponse<object>
            {
                Code = 400,
                Message = firstErrorMessage
            };
            return new BadRequestObjectResult(response);
        };
    });

// 插件加载
var loadedPlugins = new List<LoadedPlugin>();
var pluginsPath = Path.Combine(IConfigBase.GetAppDataPath(), "Plugins");
using var pluginLoggerSource = LoggerFactory.Create(l => l.AddConsole());
var pluginLogger = pluginLoggerSource.CreateLogger("PluginLoader");
if (Directory.Exists(pluginsPath))
{
    foreach (var dllPath in Directory.GetFiles(pluginsPath, "*.dll"))
    {
        try
        {
            var assembly = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(dllPath);
            
            var pluginType = assembly.GetTypes().FirstOrDefault(t => 
                typeof(MSLX.SDK.IPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

            if (pluginType != null)
            {
                var pluginInstance = (MSLX.SDK.IPlugin)Activator.CreateInstance(pluginType)!;
                
                pluginManager.Plugins.Add(new LoadedPlugin { 
                    Assembly = assembly, 
                    Metadata = pluginInstance 
                });
                
                // 注册 API
                mvcBuilder.PartManager.ApplicationParts.Add(new Microsoft.AspNetCore.Mvc.ApplicationParts.AssemblyPart(assembly));
                loadedPlugins.Add(new LoadedPlugin { Assembly = assembly, Metadata = pluginInstance });
                
                pluginLogger.LogInformation($"[MSLX Plugin] 正在加载插件: {pluginInstance.Name} v{pluginInstance.Version} by @{pluginInstance.Developer}");
            }
        }
        catch (Exception ex) { pluginLogger.LogError($"[MSLX Plugin] 插件加载失败: {ex.Message}"); }
    }
}
else
{
    Directory.CreateDirectory(pluginsPath);
}

builder.Services.AddMemoryCache();

var app = builder.Build();

// 重新初始化日志
var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
var logger = loggerFactory.CreateLogger("Program"); 

IConfigBase.Initialize(loggerFactory);

logger.LogInformation("\n  __  __   ____    _      __  __\n |  \\/  | / ___|  | |     \\ \\/ /\n | |\\/| | \\___ \\  | |      \\  / \n | |  | |  ___) | | |___   /  \\ \n |_|  |_| |____/  |_____| /_/\\_\\\n                                ");
logger.LogInformation($"MSLX.Daemon 守护进程正在启动... 监听地址: {listenAddr}");
logger.LogInformation($"将使用 {IConfigBase.GetAppDataPath()} 作为应用程序数据目录。");
logger.LogInformation("欢迎使用MSLX！");

IConfigBase.ServerList = new ServerListConfig();
IConfigBase.FrpList = new FrpListConfig();
IConfigBase.TaskList = new TaskListConfig();
IConfigBase.UserList = new UserListConfig();

app.UseForwardedHeaders();
app.UseCors("AllowAll");

var embeddedProvider = new ManifestEmbeddedFileProvider(
    Assembly.GetEntryAssembly()!, 
    "wwwroot" 
);
app.UseDefaultFiles(new DefaultFilesOptions
{
    FileProvider = embeddedProvider,
    RequestPath = ""
});
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = embeddedProvider,
    RequestPath = "" 
});

// 加载插件的静态资源
foreach (var plugin in loadedPlugins)
{
    try
    {
        var fileProvider = new ManifestEmbeddedFileProvider(plugin.Assembly, "Frontend/dist");

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = fileProvider,
            RequestPath = $"/plugins/{plugin.Metadata.Id.ToLower()}/{plugin.Metadata.Version}"
        });
        logger.LogInformation($"[MSLX Plugin] 映射资源: /plugins/{plugin.Metadata.Id.ToLower()}");
    }
    catch(Exception ex)
    {
         /* 忽略无前端资源的插件 */
    }
}

// 自定义的中间件
app.UseMiddleware<BlockLoopbackMiddleware>(); 
app.UseMiddleware<AuthMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();

// 注册SignalR实时通讯
app.MapHub<CreationProgressHub>("/api/hubs/creationProgressHub");
app.MapHub<UpdateProgressHub>("/api/hubs/updateProgressHub");
app.MapHub<FrpConsoleHub>("/api/hubs/frpLogsHub");
app.MapHub<InstanceConsoleHub>("/api/hubs/instanceControlHub");
app.MapHub<SystemMonitorHub>("/api/hubs/system");
app.MapHub<DaemonUpdateHub>("/api/hubs/daemonUpdate");
app.MapControllers();

// SPA
app.MapFallbackToFile("index.html", new StaticFileOptions
{
    FileProvider = embeddedProvider
});

var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
// 启动事件
lifetime.ApplicationStarted.Register(() =>
{
    logger.LogInformation("MSLX 守护进程服务已就绪！欢迎使用~");
    var pluginManager = app.Services.GetRequiredService<PluginManager>();

    // 调用插件的初始化方法
    int successCount = 0;
    foreach (var plugin in pluginManager.Plugins)
    {
        try
        {
            plugin.Metadata.OnLoad();

            logger.LogInformation($"[MSLX Plugin] 插件业务已启动: {plugin.Metadata.Name}");
            successCount++;
        }
        catch (Exception ex)
        {
            logger.LogError($"[MSLX Plugin] 插件 {plugin.Metadata.Name} 启动失败 (OnLoad 异常): {ex.Message}");
        }
    }

    if (pluginManager.Plugins.Count > 0)
    {
        logger.LogInformation($"[MSLX Plugin] 插件加载完毕，共 {successCount}/{pluginManager.Plugins.Count} 个插件成功运行。");
    }

});
// 关闭事件
lifetime.ApplicationStopping.Register(() =>
{
    logger.LogInformation("MSLX 守护进程正在停止，正在释放插件资源...");

    var pluginManager = app.Services.GetRequiredService<PluginManager>();

    foreach (var plugin in pluginManager.Plugins)
    {
        try
        {
            plugin.Metadata.OnUnload();
            logger.LogInformation($"[MSLX Plugin] 插件已卸载: {plugin.Metadata.Name}");
        }
        catch (Exception ex)
        {
            logger.LogError($"[MSLX Plugin] 插件 {plugin.Metadata.Name} 卸载时发生异常: {ex.Message}");
        }
    }
});

// 显示实例化服务
app.Services.GetService<IFrpProcessService>();
app.Services.GetService<IMCServerService>();

logger.LogInformation("正在检查 MSLAPI V3 主服务连通性...");
try
{
    var (success, data, msg) = await MSLApi.GetDataAsync("/");

    if (success && data is Newtonsoft.Json.Linq.JToken jsonData)
    {
        var uid = jsonData["userInfo"]?["uid"]?.ToString();
        var regtime = jsonData["userInfo"]?["regTime"]?.ToString();
        logger.LogInformation($"MSLAPI V3 主服务连接成功！当前设备 UID: {uid}，注册时间: {regtime}");
    }
    else
    {
        logger.LogWarning($"MSLAPI V3 主服务连接异常 ({msg})，尝试切换至备用 API...");

        // 切换备用地址
        MSLApi.ApiUrl = "https://api.mslmc.net/v4";

        var (backupSuccess, _, backupMsg) = await MSLApi.GetDataAsync("/");
        if (backupSuccess)
        {
            logger.LogInformation("已成功切换并连接到备用 API服务！");
        }
        else
        {
            logger.LogWarning($"备用 API 同样无法连接 ({backupMsg})，按现有配置继续运行。");
        }
    }
}
catch (Exception ex)
{
    logger.LogError($"API 检测阶段发生未捕获的异常: {ex.Message}。进程将继续运行。");
}

MSLX.SDK.MSLX.Initialize(new MSLX.Daemon.Adapters.DaemonConfigProvider());

app.Run();

