using System.Reflection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using MSLX.Daemon.Hubs;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.BackgroundTasks;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.Daemon.Middleware;
using MSLX.Daemon.Models;
using MSLX.Daemon.Services;

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
builder.Services.AddSingleton<FrpProcessService>();
builder.Services.AddSingleton(typeof(IBackgroundTaskQueue<>), typeof(BackgroundTaskQueue<>));
builder.Services.AddSingleton<MCServerService>();
builder.Services.AddSingleton<SystemMonitor>();

// 后台服务注册
builder.Services.AddHostedService<ServerCreationService>();
builder.Services.AddHostedService<ServerUpdateService>();
builder.Services.AddHostedService<TempFileCleanupService>();
builder.Services.AddHostedService<TaskSchedulerService>();
builder.Services.AddHostedService<SystemMonitorWorker>();

// 瞬时服务注册
builder.Services.AddScoped<JavaScannerService>();
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
builder.Services.AddControllers()
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

builder.Services.AddMemoryCache();

var app = builder.Build();

// 重新初始化日志
var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
var logger = loggerFactory.CreateLogger("Program"); 

IConfigBase.Initialize(loggerFactory);

logger.LogInformation("\n  __  __   ____    _      __  __\n |  \\/  | / ___|  | |     \\ \\/ /\n | |\\/| | \\___ \\  | |      \\  / \n | |  | |  ___) | | |___   /  \\ \n |_|  |_| |____/  |_____| /_/\\_\\\n                                ");
logger.LogInformation($"MSLX.Daemon 守护进程正在启动... 监听地址: {listenAddr}");
logger.LogInformation($"将使用 {IConfigBase.GetAppDataPath()} 作为应用程序数据目录。");
logger.LogInformation("欢迎使用！");

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
app.MapControllers();

// SPA
app.MapFallbackToFile("index.html", new StaticFileOptions
{
    FileProvider = embeddedProvider
});

var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStarted.Register(() =>
{
    if((bool?)IConfigBase.Config.ReadConfig()["openWebConsoleOnLaunch"] ?? true)
    {
        PlatFormServices.OpenBrowser($"{listenAddr}");
    }
});

app.Run();