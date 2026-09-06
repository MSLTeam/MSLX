using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MSLX.Daemon.Adapters;
using MSLX.Daemon.Hubs;
using MSLX.Daemon.Middleware;
using MSLX.Daemon.Services;
using MSLX.Daemon.Services.DeployServerService;
using MSLX.Daemon.Services.PluginsService;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.BackgroundTasks;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.IServices;
using MSLX.SDK.Models;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using System.Reflection;
using MSLX.Daemon.Services.ResourceServices;

System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

var builder = WebApplication.CreateBuilder(args);


// 日志配置
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore.Hosting.Diagnostics", Serilog.Events.LogEventLevel.Warning)
    .WriteTo.Console(
        theme: AnsiConsoleTheme.Sixteen,
        outputTemplate: "{Level:w4}: {SourceContext}[{EventId}]{NewLine}      {Message:lj}{NewLine}{Exception}"
    )
    .WriteTo.File(
        path: Path.Combine(IConfigBase.GetAppDataPath(), "Logs", "mslx-daemon-log-.txt"),         // 存放在 Logs 文件夹下
        rollingInterval: RollingInterval.Day, // 按天生成文件
        retainedFileCountLimit: 5,            // 只保留最新 5 个文件
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff local}] [{Level:w4}] {SourceContext}[{EventId}] {Message:lj}{NewLine}{Exception}"
    )
    .CreateLogger();

builder.Host.UseSerilog();

// 创建临时 Logger
var bootstrapLoggerFactory = LoggerFactory.Create(logging => logging.AddSerilog());
IConfigBase.Initialize(bootstrapLoggerFactory);

// 内置 Daemon 由 Desktop 管理监听策略，默认仅本机访问，也可显式开启外部访问。
bool isEmbeddedDaemon = string.Equals(
    Environment.GetEnvironmentVariable("MSLX_EMBEDDED_DAEMON"),
    "true",
    StringComparison.OrdinalIgnoreCase);

// 检查启动参数
var argHost = builder.Configuration["host"]; // 支持 --host 或 /host
var argPort = builder.Configuration["port"]; // 支持 --port 或 /port
var argNoBrowser = builder.Configuration["nobrowser"]; // 支持 --nobrowser 或 /nobrowser
bool configUpdated = false;

//传入了 host 参数，更新配置
if (!isEmbeddedDaemon && !string.IsNullOrWhiteSpace(argHost))
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

// 子节点相关的参数 --slave / --linkkey
var argSlave = builder.Configuration["slave"];
var argLinkKey = builder.Configuration["linkkey"];

bool hasSlaveSwitch = args.Any(a => a.Equals("--slave", StringComparison.OrdinalIgnoreCase) || a.Equals("/slave", StringComparison.OrdinalIgnoreCase));

if (hasSlaveSwitch || !string.IsNullOrWhiteSpace(argSlave))
{
    bool isSlave = true;
    if (!string.IsNullOrWhiteSpace(argSlave) && bool.TryParse(argSlave, out bool parsedSlave))
    {
        isSlave = parsedSlave;
    }
    
    IConfigBase.Config.WriteConfigKey("IsSlaveMode", isSlave);
    configUpdated = true;
    
    if (isSlave)
    {
        string currentKey = IConfigBase.Config.ReadConfigKey("SlaveLinkKey")?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(argLinkKey) && string.IsNullOrWhiteSpace(currentKey))
        {
            argLinkKey = StringServices.GenerateRandomString(32);
        }
    }
}

if (!string.IsNullOrWhiteSpace(argLinkKey))
{
    IConfigBase.Config.WriteConfigKey("SlaveLinkKey", argLinkKey);
    configUpdated = true;
}

if (configUpdated)
{
    var loggerTemp = LoggerFactory.Create(l => l.AddConsole()).CreateLogger("Bootstrap");
    var slaveModeVal = IConfigBase.Config.ReadConfigKey("IsSlaveMode")?.ToString() ?? "false";
    var linkKeyVal = IConfigBase.Config.ReadConfigKey("SlaveLinkKey")?.ToString() ?? "未设置";
    loggerTemp.LogInformation($"检测到启动参数，配置已更新为 Host: {argHost}, Port: {argPort}, IsSlaveMode: {slaveModeVal}, SlaveLinkKey: {linkKeyVal}");
}

// 读取最终配置
var finalConfig = IConfigBase.Config.ReadConfig();
string finalIp = isEmbeddedDaemon
    ? "localhost"
    : finalConfig["listenHost"]?.ToString() ?? "";
string finalPort = finalConfig["listenPort"]?.ToString() ?? "";
bool allowExternalAccess = false;
if (isEmbeddedDaemon)
{
    bool.TryParse(finalConfig["allowExternalAccess"]?.ToString(), out allowExternalAccess);
}

// 默认值回退
string targetIp = string.IsNullOrEmpty(finalIp) ? "localhost" : finalIp;
string targetPort = string.IsNullOrWhiteSpace(finalPort) ? "1027" : finalPort;
int port = int.Parse(targetPort);

// 检测SSL开启状态
bool enableSsl = (bool?)IConfigBase.Config.ReadConfig()["enableSsl"] ?? false;
string protocol = enableSsl ? "https" : "http";
string listenAddr = isEmbeddedDaemon && allowExternalAccess
    ? $"{protocol}://0.0.0.0:{targetPort} 和 {protocol}://[::]:{targetPort}"
    : $"{protocol}://{targetIp}:{targetPort}";

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    Action<Microsoft.AspNetCore.Server.Kestrel.Core.ListenOptions> configureListen = listenOptions =>
    {
        if (enableSsl)
        {
            SslCertificateManager.ReloadCertificate();

            listenOptions.UseHttps(httpsOptions =>
            {
                httpsOptions.ServerCertificateSelector = (context, domain) =>
                {
                    return SslCertificateManager.GetCertificate(); 
                };
            });
        }
    };
    
    if (isEmbeddedDaemon && allowExternalAccess)
    {
        // Kestrel 的 ListenAnyIP 使用 IPv6Any，并在支持的平台上启用双栈，
        // 对应监听 IPv4 的 0.0.0.0 和 IPv6 的 [::]。
        serverOptions.ListenAnyIP(port, configureListen);
    }
    else if (targetIp == "0.0.0.0" || targetIp == "*")
    {
        serverOptions.ListenAnyIP(port, configureListen);
    }
    else if (targetIp.ToLower() == "localhost")
    {
        serverOptions.ListenLocalhost(port, configureListen);
    }
    else
    {
        serverOptions.Listen(System.Net.IPAddress.Parse(targetIp), port, configureListen);
    }
});

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
builder.Services.AddSingleton<IDockerService,DockerService>();
builder.Services.AddSingleton<SystemMonitor>();
builder.Services.AddSingleton<CreationTaskTracker>();
builder.Services.AddSingleton<BackgroundTaskManager>();
builder.Services.AddSingleton<IBackgroundTaskManager>(sp => sp.GetRequiredService<BackgroundTaskManager>());
builder.Services.AddSingleton<AiService>();
// 插件的一些服务
var pluginManager = new PluginManager();
builder.Services.AddSingleton(pluginManager);
builder.Services.AddSingleton<Microsoft.AspNetCore.Mvc.Infrastructure.IActionDescriptorChangeProvider>(HotReloadActionDescriptorChangeProvider.Instance);
builder.Services.Replace(ServiceDescriptor.Transient<Microsoft.AspNetCore.Mvc.Controllers.IControllerActivator, PluginControllerActivator>());
builder.Services.Replace(ServiceDescriptor.Transient(typeof(Microsoft.AspNetCore.SignalR.IHubActivator<>), typeof(MSLX.Daemon.Services.PluginsService.PluginHubActivator<>)));

// 后台服务注册
builder.Services.AddHostedService<ServerCreationService>();
builder.Services.AddHostedService<ServerUpdateService>();
builder.Services.AddHostedService<TempFileCleanupService>();
builder.Services.AddHostedService<TaskSchedulerService>();
builder.Services.AddHostedService<SystemMonitorWorker>();

// 瞬时服务注册
builder.Services.AddScoped<IJavaScannerService,JavaScannerService>();
builder.Services.AddScoped<IPythonScannerService,PythonScannerService>();
builder.Services.AddTransient<NeoForgeInstallerService>();
builder.Services.AddTransient<ServerDeploymentService>();

// 资源下载相关服务注册
builder.Services.AddHttpClient<IResourceProvider, CurseForgeService>();
builder.Services.AddHttpClient<IResourceProvider, ModrinthService>();
builder.Services.AddTransient<IUnifiedResourceService, UnifiedResourceService>();

// 配置真实IP回传协议
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear(); 
    options.KnownProxies.Clear();
});

// 错误中间件
var mvcBuilder = builder.Services.AddControllers()
    .AddNewtonsoftJson()
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

// 插件删除 & 更新（旧版逻辑 暂时保留 大概率用不上的了）

var pluginsPath = Path.Combine(IConfigBase.GetAppDataPath(), "Plugins");
var pluginLogger = bootstrapLoggerFactory.CreateLogger("PluginLoader");

if (!Directory.Exists(pluginsPath))
{
    Directory.CreateDirectory(pluginsPath);
}
else
{
    // 处理删除插件
    foreach (var deleteFile in Directory.GetFiles(pluginsPath, "*.dll.delete"))
    {
        var targetDll = deleteFile.Substring(0, deleteFile.Length - 7); 
        try
        {
            if (File.Exists(targetDll)) 
            {
                File.Delete(targetDll);
            }
            File.Delete(deleteFile);
            pluginLogger.LogInformation($"[MSLX Plugin] 已清理待删除插件文件: {Path.GetFileName(targetDll)}");
        }
        catch (Exception ex)
        {
            pluginLogger.LogWarning($"[MSLX Plugin] 无法删除插件文件 {Path.GetFileName(targetDll)}: {ex.Message}");
        }
    }

    // 处理插件更新
    foreach (var newFile in Directory.GetFiles(pluginsPath, "*.dll.new"))
    {
        var targetDll = newFile.Substring(0, newFile.Length - 4);
        try
        {
            if (File.Exists(targetDll)) 
            {
                File.Delete(targetDll);
            }
            File.Move(newFile, targetDll);
            pluginLogger.LogInformation($"[MSLX Plugin] 已应用插件更新/安装: {Path.GetFileName(targetDll)}");
        }
        catch (Exception ex)
        {
            pluginLogger.LogWarning($"[MSLX Plugin] 无法应用插件更新 {Path.GetFileName(targetDll)}: {ex.Message}");
        }
    }
}

builder.Services.AddMemoryCache();

var app = builder.Build();

// 重新初始化日志
var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
var logger = loggerFactory.CreateLogger("Program");

// 初始化 PluginManager 服务
var partManager = app.Services.GetRequiredService<Microsoft.AspNetCore.Mvc.ApplicationParts.ApplicationPartManager>();
pluginManager.Initialize(app.Services, partManager, loggerFactory.CreateLogger<PluginManager>(), builder.Services, app);

// 注册全局动态高级路由源
var dataSources = ((Microsoft.AspNetCore.Routing.IEndpointRouteBuilder)app).DataSources;
dataSources.Add(pluginManager.DynamicEndpoints);


// 注册代理方法给SDK
MSLX.SDK.MSLX.Initialize(
    new DaemonConfigProvider(),
    new DaemonLoggerProvider(loggerFactory),
    new DaemonDownloadProvider(),
    new DaemonHttpProvider(),
    app.Services.GetRequiredService<IBackgroundTaskManager>()
);

// 插件初始化方法
if (Directory.Exists(pluginsPath))
{
    foreach (var dllPath in Directory.GetFiles(pluginsPath, "*.dll"))
    {
        pluginManager.LoadPlugin(dllPath);
    }
}

IConfigBase.Initialize(loggerFactory);

logger.LogInformation("\n  __  __   ____    _      __  __\n |  \\/  | / ___|  | |     \\ \\/ /\n | |\\/| | \\___ \\  | |      \\  / \n | |  | |  ___) | | |___   /  \\ \n |_|  |_| |____/  |_____| /_/\\_\\\n                                ");
logger.LogInformation($"MSLX.Daemon 守护进程正在启动... 监听地址: {listenAddr}");
logger.LogInformation($"将使用 {IConfigBase.GetAppDataPath()} 作为应用程序数据目录。");
logger.LogInformation("欢迎使用MSLX！");

IConfigBase.ServerList = new ServerListConfig();
IConfigBase.FrpList = new FrpListConfig();
IConfigBase.TaskList = new TaskListConfig();
IConfigBase.UserList = new UserListConfig();
IConfigBase.NodeList = new NodeListConfig();
IConfigBase.MasterNodes = new MasterNodesConfig();

bool isSlaveStartup = bool.Parse(IConfigBase.Config.ReadConfigKey("IsSlaveMode")?.ToString() ?? "false");
if (isSlaveStartup)
{
    logger.LogInformation("当前运行模式: 子节点模式");
}
else
{
    logger.LogInformation("当前运行模式: 主控模式");
}

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

Action<StaticFileResponseContext> setCacheControl = ctx =>
{
    var fileName = ctx.File?.Name ?? "";
    var ext = Path.GetExtension(fileName).ToLowerInvariant();
    var contentType = ctx.Context.Response.ContentType ?? "";

    // HTML响应 缓存3分钟
    if (ext == ".html" || ext == ".htm" || contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
    {
        ctx.Context.Response.Headers.CacheControl = "public, max-age=180";
        return;
    }

    switch (ext)
    {
        case ".js":
        case ".css":
        case ".png":
        case ".jpg":
        case ".jpeg":
        case ".gif":
        case ".ico":
        case ".svg":
        case ".woff":
        case ".woff2":
        case ".ttf":
        case ".eot":
        case ".webp":
        case ".avif":
        case ".mp4":
        case ".webm":
            ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            break;

        default:
            ctx.Context.Response.Headers.CacheControl = "public, max-age=300";
            break;
    }
};

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = embeddedProvider,
    RequestPath = "",
    OnPrepareResponse = setCacheControl
});

// 动态加载插件静态资源
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new MSLX.Daemon.Services.PluginsService.PluginDynamicFileProvider(pluginManager),
    RequestPath = "/plugins",
    OnPrepareResponse = setCacheControl
});

app.UseRouting();

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
app.MapHub<AiChatHub>("/api/hubs/aiChatHub");
app.MapControllers();


// SPA
app.MapFallbackToFile("index.html", new StaticFileOptions
{
    FileProvider = embeddedProvider,
    OnPrepareResponse = setCacheControl
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

            logger.LogInformation($"[MSLX Plugin] 插件已成功加载: {plugin.Metadata.Name}");
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
        var publicSource = jsonData["apiInfo"]?["downloadSources"]?["public"]?.ToString();
        MSLApi.SetDownloadDomainFromPublicUrl(publicSource);

        logger.LogInformation($"MSLAPI V3 主服务连接成功！当前设备 UID: {uid}，注册时间: {regtime}，下载源域名: {MSLApi.DownloadDomain}");
    }
    else
    {
        logger.LogWarning($"MSLAPI V3 主服务连接异常 ({msg})，尝试切换至备用 API...");

        // 切换备用地址
        MSLApi.ApiUrl = "https://api.mslmc.net/v4";

        var (backupSuccess, backupData, backupMsg) = await MSLApi.GetDataAsync("/");
        if (backupSuccess)
        {
            if (backupData is Newtonsoft.Json.Linq.JToken backupJsonData)
            {
                var publicSource = backupJsonData["apiInfo"]?["downloadSources"]?["public"]?.ToString();
                MSLApi.SetDownloadDomainFromPublicUrl(publicSource);
            }
            logger.LogInformation($"已成功切换并连接到备用 API服务！当前下载源域名: {MSLApi.DownloadDomain}");
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


try
{
    app.Run();
}
finally
{
    Log.CloseAndFlush();
}
