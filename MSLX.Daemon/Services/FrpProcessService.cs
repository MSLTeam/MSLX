using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.SignalR;
using MSLX.Daemon.Hubs;
using MSLX.Daemon.Utils;

namespace MSLX.Daemon.Services;

public class FrpProcessService
{
    private readonly ILogger<FrpProcessService> _logger;
    private readonly IHubContext<FrpConsoleHub> _hubContext; 

    // 捆绑进程对象和缓存日志的类
    public class FrpContext
    {
        public Process Process { get; set; } = null!;
        public ConcurrentQueue<string> Logs { get; set; } = new();
    }

    // 字典 ID -> Context
    private readonly ConcurrentDictionary<int, FrpContext> _activeProcesses = new();

    // 最大日志缓存行数
    private const int MaxLogLines = 200;
    
    private readonly string _frpcExecutablePath;

    // 注入构造函数
    public FrpProcessService(ILogger<FrpProcessService> logger, IHubContext<FrpConsoleHub> hubContext)
    {
        _logger = logger;
        _hubContext = hubContext;
        
        // TODO: 下载Frpc
        string baseDir = ConfigServices.GetAppDataPath();
        string exeName = PlatFormServices.GetOs() == "Windows" ? "frpc.exe" : "frpc";
        _frpcExecutablePath = Path.Combine(baseDir, "DaemonData", "Tools", exeName);
    }

    public bool IsFrpRunning(int id)
    {
        if (_activeProcesses.TryGetValue(id, out var context))
        {
            if (context.Process != null && !context.Process.HasExited)
            {
                return true;
            }
            _activeProcesses.TryRemove(id, out _);
        }
        return false;
    }

    /// <summary>
    /// 启动 FRP 进程
    /// </summary>
    public (bool success, string message) StartFrp(int id)
    {
        if (IsFrpRunning(id)) return (false, "该隧道已经在运行中");
        
        var frpConfig = ConfigServices.FrpList.GetFrpConfig(id);
        if (frpConfig == null) return (false, "找不到指定的配置");

        string configType = frpConfig["ConfigType"]?.ToString() ?? "ini";
        
        string configFolder = Path.Combine(ConfigServices.GetAppDataPath(), "DaemonData", "Configs", "Frpc", id.ToString());
        string configFilePath = Path.Combine(configFolder, $"frpc.{configType}");

        if (!File.Exists(_frpcExecutablePath)) 
            return (false, $"核心文件不存在: {_frpcExecutablePath}");
        
        if (!File.Exists(configFilePath)) 
            return (false, $"配置文件不存在: {configFilePath}");

        try
        {
            // 在非Win系统下需要赋予可执行权限
            GrantExecutePermission(_frpcExecutablePath);

            // 构建启动参数
            var startInfo = new ProcessStartInfo
            {
                FileName = _frpcExecutablePath,
                Arguments = $"-c \"{configFilePath}\"", // 配置文件
                RedirectStandardOutput = true, // 重定向标准输出
                RedirectStandardError = true,  // 重定向错误输出
                UseShellExecute = false,       // 只能是false喵
                CreateNoWindow = true,         // 不创建新窗口
                StandardOutputEncoding = Encoding.UTF8, // 编码在这
                StandardErrorEncoding = Encoding.UTF8
            };

            var process = new Process { StartInfo = startInfo };
            var context = new FrpContext { Process = process };

            // 监听日志流
            process.OutputDataReceived += (sender, e) => RecordLog(id, context, e.Data);
            process.ErrorDataReceived += (sender, e) => RecordLog(id, context, e.Data);
            RecordLog(id, context, "[MSLX] Frpc进程开始启动...");
            // 启动！
            process.Start();
            
            // 开始读取
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // 丢到字典里存着
            _activeProcesses[id] = context;

            _logger.LogInformation($"FRP 隧道 [{id}] 启动成功，PID: {process.Id}");
            return (true, "启动成功");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"FRP 隧道 [{id}] 启动失败");
            return (false, $"启动异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 停止 FRP 进程
    /// </summary>
    public bool StopFrp(int id)
    {
        if (_activeProcesses.TryRemove(id, out var context))
        {
            try
            {
                if (!context.Process.HasExited)
                {
                    context.Process.Kill(); // 强制结束
                    context.Process.WaitForExit(1000); 
                }
                
                RecordLog(id, context, "[MSLX] Frpc进程已经退出...");
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "停止进程时出错");
            }
        }
        return false;
    }

    /// <summary>
    /// 获取指定 ID 的最近日志 (用于前端刚打开控制台时加载历史)
    /// </summary>
    public List<string> GetLogs(int id)
    {
        if (_activeProcesses.TryGetValue(id, out var context))
        {
            return context.Logs.ToList();
        }
        return new List<string>();
    }

    // 记录日志的方法

    private void RecordLog(int frpId, FrpContext context, string? data)
    {
        if (string.IsNullOrWhiteSpace(data)) return;
        
        context.Logs.Enqueue(data);

        // 限制缓存大小
        while (context.Logs.Count > MaxLogLines)
        {
            context.Logs.TryDequeue(out _);
        }

        // SignalR 实时推送
        _hubContext.Clients.Group(frpId.ToString()).SendAsync("ReceiveLog", data);
    }

    // 赋予权限的复制函数
    private void GrantExecutePermission(string filePath)
    {
        string os = PlatFormServices.GetOs();
        if (os == "Windows") return;

        try
        {
            var info = new ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"+x \"{filePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var proc = Process.Start(info);
            proc?.WaitForExit();
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"赋予执行权限失败 (非致命): {ex.Message}");
        }
    }
}