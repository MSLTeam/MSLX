using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using MSLX.Daemon.Hubs;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Middleware;
using MSLX.Daemon.Models;
using MSLX.Daemon.Services;
using MSLX.Daemon.Utils.BackgroundTasks;
using MSLX.Daemon.Utils.BackgroundTasks.BackgroundTasks;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers()
    .AddNewtonsoftJson();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

string listenAddr = "http://localhost:1027";
builder.WebHost.UseUrls(listenAddr);

builder.Services.AddCors(options =>
{
    options.AddPolicy(name: "AllowAll",
        policy  =>
        {
            policy.AllowAnyOrigin() // 允许任何源
                .AllowAnyHeader() // 允许任何 Header
                .AllowAnyMethod(); // 允许任何方法
        });
});

builder.Services.AddSignalR();

// 注册单例服务
builder.Services.AddSingleton<FrpProcessService>();
builder.Services.AddSingleton<IBackgroundTaskQueue>(ctx =>
{
    // 队列容量
    return new BackgroundTaskQueue(100);
});

builder.Services.AddHostedService<ServerCreationService>();
builder.Services.AddHostedService<TempFileCleanupService>();
builder.Services.AddScoped<MCServerService>();
builder.Services.AddScoped<JavaScannerService>();
builder.Services.AddTransient<NeoForgeInstallerService>();


// 配置转发头选项
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    // 处理 X-Forwarded-For (IP) 和 X-Forwarded-Proto (协议 http/https)
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    
    // 需要清空已知代理列表
    options.KnownNetworks.Clear(); 
    options.KnownProxies.Clear();
});

// 覆盖默认的模型验证失败响应
builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var firstErrorMessage = context.ModelState.Values
                .SelectMany(v => v.Errors)
                .FirstOrDefault()?.ErrorMessage ?? "请求参数验证失败"; // 提供一个默认错误
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

// 初始化日志服务
var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();

var logger = loggerFactory.CreateLogger("Program"); 

logger.LogInformation("\n  __  __   ____    _      __  __\n |  \\/  | / ___|  | |     \\ \\/ /\n | |\\/| | \\___ \\  | |      \\  / \n | |  | |  ___) | | |___   /  \\ \n |_|  |_| |____/  |_____| /_/\\_\\\n                                ");
logger.LogInformation("MSLX.Daemon 守护进程正在启动...");
logger.LogInformation("应用程序已构建 (app.Build() 已完成)");
logger.LogInformation("正在配置中间件 (Middleware)...");
logger.LogInformation($"将使用 {ConfigServices.GetAppDataPath()} 作为应用程序数据目录。");

// 初始化配置服务
ConfigServices.Initialize(loggerFactory);
logger.LogInformation("欢迎您！" + ConfigServices.Config.ReadConfigKey("user"));

app.UseForwardedHeaders();

app.UseCors("AllowAll");
app.UseMiddleware<BlockLoopbackMiddleware>(); // 可配置的拦截本地访问中间件
app.UseMiddleware<ApiKeyMiddleware>(); // APIKey校验中间件



// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();

// 注册SignalR实时通信服务
app.MapHub<CreationProgressHub>("/api/hubs/creationProgressHub");
app.MapHub<FrpConsoleHub>("/api/hubs/frpLogsHub");
app.MapControllers();

// 注册启动事件
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStarted.Register(() =>
{
    // 打开控制台(仅当本地地址才开)
    if(((bool?)ConfigServices.Config.ReadConfig()["openWebConsoleOnLaunch"] ?? true) && listenAddr.Contains("localhost") || listenAddr.Contains("127.0.0.1"))
    {
        var address = "https://alpha-mslx.aino.cyou/login";
        PlatFormServices.OpenBrowser($"{address}?auth={StringServices.EncodeToBase64($"{listenAddr}|{ConfigServices.Config.ReadConfigKey("apiKey")}")}");
    }
});

app.Run();

