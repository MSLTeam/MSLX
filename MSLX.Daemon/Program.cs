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
builder.WebHost.UseUrls("http://localhost:1027");

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
builder.Services.AddSingleton<IBackgroundTaskQueue>(ctx =>
{
    // 队列容量
    return new BackgroundTaskQueue(100);
});
builder.Services.AddHostedService<ServerCreationService>();

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
                code = 400,
                message = firstErrorMessage
            };
            
            return new BadRequestObjectResult(response);
        };
    });

builder.Services.AddMemoryCache();

var app = builder.Build();

// 初始化日志服务
var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();

var logger = loggerFactory.CreateLogger("Program"); 

logger.LogInformation("  __  __   ____    _      __  __\n |  \\/  | / ___|  | |     \\ \\/ /\n | |\\/| | \\___ \\  | |      \\  / \n | |  | |  ___) | | |___   /  \\ \n |_|  |_| |____/  |_____| /_/\\_\\\n                                ");
logger.LogInformation("MSLX.Daemon 守护进程正在启动...");
logger.LogInformation("应用程序已构建 (app.Build() 已完成)");
logger.LogInformation("正在配置中间件 (Middleware)...");
logger.LogInformation($"将使用 {ConfigServices.GetAppDataPath()} 作为应用程序数据目录。");

// 初始化配置服务
ConfigServices.Initialize(loggerFactory);
logger.LogInformation("欢迎您！" + ConfigServices.Config.ReadConfigKey("user"));

app.UseCors("AllowAll");
app.UseMiddleware<ApiKeyMiddleware>();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();

// 注册SignalR实时通信服务
app.MapHub<CreationProgressHub>("/api/hubs/creationProgressHub");
app.MapControllers();

app.Run();

