using MSLX.Daemon.Utils;
using MSLX.Daemon.Middleware;

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

app.MapControllers();

app.Run();

