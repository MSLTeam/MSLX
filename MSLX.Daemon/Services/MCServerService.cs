using Microsoft.AspNetCore.SignalR;
using MSLX.Daemon.Hubs;
using MSLX.Daemon.Models;
using MSLX.Daemon.Utils;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Management;
using System.Text;
using Newtonsoft.Json.Linq;

namespace MSLX.Daemon.Services;

public class MCServerService
{
    private readonly ILogger<MCServerService> _logger;
    private readonly IHubContext<InstanceConsoleHub> _hubContext;
    private readonly IHostApplicationLifetime _appLifetime;

    // 短时间内崩溃重启限制
    private readonly ConcurrentDictionary<uint, List<DateTime>> _crashHistory = new();
    private const int CrashCheckWindowSeconds = 300;
    private const int MaxCrashCount = 5;

    public class ServerContext
    {
        public Process? Process { get; set; }
        public ConcurrentQueue<string> Logs { get; set; } = new();
        public bool IsInitializing { get; set; } = false;
        public volatile bool IsStopping = false;
        public volatile bool IsBackuping = false;

        // 用于计算资源使用率
        public TimeSpan PreviousTotalProcessorTime { get; set; } = TimeSpan.Zero;
        public DateTime PreviousCpuCheckTime { get; set; } = DateTime.MinValue;

        public Process? MonitorProcess { get; set; } // win下监控的进程
        public int LastMonitoredPid { get; set; } = -1;
    }

    private readonly ConcurrentDictionary<uint, ServerContext> _activeServers = new();
    private const int MaxLogLines = 1000;

    public MCServerService(
        ILogger<MCServerService> logger,
        IHubContext<InstanceConsoleHub> hubContext,
        IHostApplicationLifetime appLifetime)
    {
        _logger = logger;
        _hubContext = hubContext;
        _appLifetime = appLifetime;

        _appLifetime.ApplicationStopping.Register(StopAllServers);
        _appLifetime.ApplicationStarted.Register(OnAppStarted);

        // 注册编码提供程序，以支持 GBK 等非 Unicode 编码
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// 检查服务器是否正在运行
    /// </summary>
    public bool IsServerRunning(uint instanceId)
    {
        if (_activeServers.TryGetValue(instanceId, out var context))
        {
            if (context.IsInitializing) return true;

            if (context.Process != null && !context.Process.HasExited)
            {
                return true;
            }

            _activeServers.TryRemove(instanceId, out _);
        }

        return false;
    }

    /// <summary>
    /// 启动 MC 服务器 (非阻塞模式)
    /// </summary>
    public (bool success, string message) StartServer(uint instanceId)
    {
        if (IsServerRunning(instanceId))
            return (false, "该服务器已经在运行中或正在启动中");

        _crashHistory.TryRemove(instanceId, out _);
        var serverInfo = ConfigServices.ServerList.GetServer(instanceId);
        if (serverInfo == null)
            return (false, "找不到指定的服务器配置");

        var context = new ServerContext { IsInitializing = true };
        _activeServers[instanceId] = context;

        // 后台任务启动服务器
        _ = Task.Run(async () => await InternalStartServerAsync(instanceId, context, serverInfo));

        return (true, "正在启动服务器...");
    }

    /// <summary>
    /// 辅助方法：根据字符串获取 Encoding
    /// </summary>
    private Encoding GetEncoding(string? encodingName)
    {
        // 如果为空，默认返回无 BOM 的 UTF-8
        if (string.IsNullOrWhiteSpace(encodingName))
            return new UTF8Encoding(false);

        try
        {
            // 归一化编码名称
            var name = encodingName.Trim().ToLower();

            // 特殊处理 UTF-8，强制禁用 BOM
            if (name == "utf-8" || name == "utf8")
            {
                return new UTF8Encoding(false);
            }

            // 其他编码（如 GBK, Big5）通常不带 BOM，直接获取即可
            return Encoding.GetEncoding(name);
        }
        catch (Exception)
        {
            _logger.LogWarning($"无法识别编码: {encodingName}，已回退到 UTF-8 (No BOM)");
            // 回退时也必须使用无 BOM 的 UTF-8
            return new UTF8Encoding(false);
        }
    }

    /// <summary>
    /// 异步启动服务器
    /// </summary>
    private async Task InternalStartServerAsync(uint instanceId, ServerContext context,
        McServerInfo.ServerInfo serverInfo)
    {
        try
        {
            RecordLog(instanceId, context, "[MSLX] 正在初始化服务...");
            // 检查核心文件是否存在
            if (serverInfo.Core != "none" && !serverInfo.Core.Contains("@libraries"))
            {
                string coreFilePath = Path.Combine(serverInfo.Base, serverInfo.Core);
                if (!File.Exists(coreFilePath))
                {
                    RecordLog(instanceId, context, $">>> [MSLX-MCServer] 核心文件不存在: {coreFilePath}");
                    _activeServers.TryRemove(instanceId, out _);
                    return;
                }
            }

            // 检查 Java 是否存在
            if (!File.Exists(serverInfo.Java) && serverInfo.Java != "java" && serverInfo.Java != "none" &&
                !serverInfo.Java.StartsWith("MSLX://Java/"))
            {
                RecordLog(instanceId, context, $">>> [MSLX-MCServer] Java 路径无效: {serverInfo.Java}");
                _activeServers.TryRemove(instanceId, out _);
                return;
            }

            if (serverInfo.Java.StartsWith("MSLX://Java/"))
            {
                string javaVersion = serverInfo.Java.Replace("MSLX://Java/", "");
                string javaBaseDir = Path.Combine(ConfigServices.GetAppDataPath(), "DaemonData", "Tools", "Java");
                string javaPath = Path.Combine(javaBaseDir, javaVersion, "bin",
                    PlatFormServices.GetOs() == "Windows" ? "java.exe" : "java");
                if (!File.Exists(javaPath))
                {
                    RecordLog(instanceId, context, $">>> [MSLX-MCServer] Java 无效！请尝试重新设置 Java 环境！");
                    _activeServers.TryRemove(instanceId, out _);
                    return;
                }
            }

            // 处理外置登录
            string authJvm = "";
            if (!string.IsNullOrEmpty(serverInfo.YggdrasilApiAddr))
            {
                if (!await DownloadAuthlib(serverInfo.Base, instanceId, context))
                {
                    RecordLog(instanceId, context, $">>> [MSLX-MCServer] 外置登录库下载失败！将不启用外置登录......");
                }
                else
                {
                    authJvm = $"-javaagent:authlib-injector.jar={serverInfo.YggdrasilApiAddr}";
                }
            }

            // 自动同意EULA
            if (serverInfo.Java != "none")
            {
                string eulaPath = Path.Combine(serverInfo.Base, "eula.txt");
                bool needAgree = false;

                // 检测文件是否存在或未同意
                if (!File.Exists(eulaPath))
                {
                    needAgree = true;
                }
                else
                {
                    string content = await File.ReadAllTextAsync(eulaPath);
                    if (!content.Contains("eula=true"))
                    {
                        needAgree = true;
                    }
                }

                if (needAgree)
                {
                    // 写入同意后的文件内容
                    string eulaFileContent =
                        $"#By changing the setting below to TRUE you are indicating your agreement to our EULA (https://aka.ms/MinecraftEULA).\n#{DateTime.Now}\neula=true";
                    await File.WriteAllTextAsync(eulaPath, eulaFileContent);

                    // 发送正式的提示信息
                    RecordLog(instanceId, context, ">>> [MSLX] 检测到 EULA 协议尚未签署，正在自动处理...");
                    RecordLog(instanceId, context,
                        "=======================================================================");
                    RecordLog(instanceId, context, "                       Minecraft 最终用户许可协议声明                   ");
                    RecordLog(instanceId, context,
                        "=======================================================================");
                    RecordLog(instanceId, context,
                        "  原文提示: \"By changing the setting below to TRUE you are indicating");
                    RecordLog(instanceId, context, "  your agreement to our EULA (https://aka.ms/MinecraftEULA).\"");
                    RecordLog(instanceId, context, "");
                    RecordLog(instanceId, context, "  [重要提示]");
                    RecordLog(instanceId, context, "  为了启动服务器，系统已自动将 eula 设置为 true。");
                    RecordLog(instanceId, context, "  此操作即代表您已阅读、知悉并同意《Minecraft 最终用户许可协议》。");
                    RecordLog(instanceId, context, "  协议地址: https://aka.ms/MinecraftEULA");
                    RecordLog(instanceId, context,
                        "=======================================================================");

                    // 强制看5秒
                    RecordLog(instanceId, context, ">>> 协议签署完成，将在 5 秒后继续启动服务器...");
                    await Task.Delay(5000);
                }
            }

            // 给予执行权限
            ExecutePermission.GrantExecutePermission(serverInfo.Base);
            await Task.Delay(100);

            string args =
                $"{authJvm} -Xms{serverInfo.MinM}M -Xmx{serverInfo.MaxM}M {serverInfo.Args} -jar {serverInfo.Core} nogui";
            string exec = serverInfo.Java;

            // 处理自定义模式参数
            if (serverInfo.Java == "none")
            {
                if (PlatFormServices.GetOs() == "Windows")
                {
                    args = $"/c {serverInfo.Args}";
                    exec = "cmd.exe";
                }
                else
                {
                    args = $"-c \"{serverInfo.Args?.Replace("\"", "\\\"")}\"";
                    exec = "/bin/bash";
                }
            }

            if (serverInfo.Java.StartsWith("MSLX://Java/"))
            {
                string javaVersion = serverInfo.Java.Replace("MSLX://Java/", "");
                string javaBaseDir = Path.Combine(ConfigServices.GetAppDataPath(), "DaemonData", "Tools", "Java");
                exec = Path.Combine(javaBaseDir, javaVersion, "bin",
                    PlatFormServices.GetOs() == "Windows" ? "java.exe" : "java");
            }

            // 处理NeoForge类型参数
            if (serverInfo.Core.Contains("@libraries"))
            {
                args =
                    $"{authJvm} -Xms{serverInfo.MinM}M -Xmx{serverInfo.MaxM}M {serverInfo.Args} {serverInfo.Core} nogui";
            }

            // 处理编码
            Encoding inputEncoding = GetEncoding(serverInfo.InputEncoding);
            Encoding outputEncoding = GetEncoding(serverInfo.OutputEncoding);
            _logger.LogDebug(
                $"实例 {instanceId} 编码设置 - 输入: {inputEncoding.EncodingName}, 输出: {outputEncoding.EncodingName}");

            // 配置启动参数
            var startInfo = new ProcessStartInfo
            {
                WorkingDirectory = serverInfo.Base,
                FileName = exec,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true, // 允许输入命令
                UseShellExecute = false,
                CreateNoWindow = true,
                // 应用编码配置
                StandardOutputEncoding = outputEncoding,
                StandardErrorEncoding = outputEncoding,
                StandardInputEncoding = inputEncoding
            };

            var process = new Process { StartInfo = startInfo };

            process.EnableRaisingEvents = true;

            // 绑定事件
            process.Exited += (sender, e) =>
            {
                if (sender is Process p)
                {
                    HandleServerExit(instanceId, p.ExitCode);
                }
            };
            process.OutputDataReceived += (sender, e) => RecordLog(instanceId, context, e.Data);
            process.ErrorDataReceived += (sender, e) => RecordLog(instanceId, context, e.Data);

            RecordLog(instanceId, context, "[MSLX] 正在启动 Minecraft 服务器...");

            // 启动进程
            if (process.Start())
            {
                context.Process = process;
                context.IsInitializing = false; // 初始化完成

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                _logger.LogInformation($"MC 服务器 [{instanceId}] 启动成功，PID: {process.Id}");
                RecordLog(instanceId, context, $"[MSLX] 服务器进程已启动，PID: {process.Id}");
            }
            else
            {
                RecordLog(instanceId, context, ">>> [MSLX-MCServer] 进程启动失败！");
                _activeServers.TryRemove(instanceId, out _);
            }
        }
        catch (Exception ex)
        {
            RecordLog(instanceId, context, $">>> [MSLX-MCServer] 启动流程发生未捕获异常: {ex.Message}");
            _logger.LogError(ex, $"MC 服务器 [{instanceId}] 启动异常");
            _activeServers.TryRemove(instanceId, out _);
        }
    }

    /// <summary>
    /// 停止服务器
    /// </summary>
    public bool StopServer(uint instanceId)
    {
        if (_activeServers.TryGetValue(instanceId, out var context))
        {
            try
            {
                context.IsStopping = true; // 标记正在停止
                if (context.Process != null && !context.Process.HasExited)
                {
                    try
                    {
                        var server = ConfigServices.ServerList.GetServer(instanceId);
                        if ((server != null && server.Java == "none") && !(server.Args ?? "").Contains("bedrock"))
                        {
                            context.Process.StandardInput.Close(); // 关闭输入流 不结束就等10s吧
                        }
                        else
                        {
                            context.Process.StandardInput.WriteLine("stop");
                            context.Process.StandardInput.Flush();
                        }

                        // 等待 10 秒
                        if (!context.Process.WaitForExit(10000))
                        {
                            // 如果 10 秒后还没关闭，强制结束
                            context.Process.Kill();
                            RecordLog(instanceId, context, "[MSLX] 服务器未能正常关闭，已强制结束");
                        }
                        else
                        {
                            RecordLog(instanceId, context, "[MSLX] 服务器已正常关闭");
                        }
                    }
                    catch
                    {
                        // 如果发送命令失败，直接强制结束
                        context.Process.Kill();
                        context.Process.WaitForExit(1000);
                        RecordLog(instanceId, context, "[MSLX] 服务器进程已强制结束");
                    }
                }

                _activeServers.TryRemove(instanceId, out _);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"停止服务器 [{instanceId}] 时出错");
                RecordLog(instanceId, context, $">>> [MSLX-MCServer] 停止进程时出错: {ex.Message}");
            }
        }

        return false;
    }

    /// <summary>
    /// 向服务器发送命令
    /// </summary>
    public bool SendCommand(uint instanceId, string command, bool repeatCommandToLog = false)
    {
        if (_activeServers.TryGetValue(instanceId, out var context))
        {
            try
            {
                if (context.Process != null && !context.Process.HasExited)
                {
                    context.Process.StandardInput.WriteLine(command);
                    context.Process.StandardInput.Flush();
                    if (repeatCommandToLog) RecordLog(instanceId, context, $"[MSLX] 已发送命令: {command}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"向服务器 [{instanceId}] 发送命令时出错");
                RecordLog(instanceId, context, $">>> [MSLX-MCServer] 发送命令失败: {ex.Message}");
            }
        }

        return false;
    }

    /// <summary>
    /// 获取服务器日志
    /// </summary>
    public List<string> GetLogs(uint instanceId)
    {
        if (_activeServers.TryGetValue(instanceId, out var context))
        {
            return context.Logs.ToList();
        }

        return new List<string>();
    }

    /// <summary>
    /// 停止所有服务器
    /// </summary>
    private void StopAllServers()
    {
        if (_activeServers.IsEmpty) return;

        _logger.LogInformation("正在停止所有 MC 服务器...");

        foreach (var kvp in _activeServers)
        {
            try
            {
                var context = kvp.Value;
                if (context.Process != null && !context.Process.HasExited)
                {
                    try
                    {
                        context.Process.StandardInput.WriteLine("stop");
                        context.Process.StandardInput.Flush();
                        context.Process.WaitForExit(5000);
                    }
                    catch
                    {
                        context.Process.Kill();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"停止服务器 [{kvp.Key}] 时出错");
            }
        }

        _activeServers.Clear();
    }

    // 启动的生命周期事件
    private void OnAppStarted()
    {
        Task.Run(async () =>
        {
            try
            {
                // 等3s 确保服务全部初始化了
                await Task.Delay(3000);

                _logger.LogInformation("[AutoStart] 正在检查自启动实例...");
                var config = ConfigServices.ServerList.GetServerList();

                int count = 0;

                foreach (var item in config)
                {
                    uint id = (uint)item.ID;
                    bool autoStart = item.RunOnStartup;

                    if (id > 0 && autoStart)
                    {
                        _logger.LogInformation($"[AutoStart] 检测到实例 [{id}] 配置为自启动，正在启动...");
                        var (success, msg) = StartServer(id);

                        if (success)
                        {
                            count++;
                            // 延迟启动
                            await Task.Delay(5000);
                        }
                        else
                        {
                            _logger.LogWarning($"[AutoStart] 实例 [{id}] 启动请求被拒绝: {msg}");
                        }
                    }
                }

                if (count > 0)
                    _logger.LogInformation($"[AutoStart] 自启动流程完成，共启动 {count} 个实例。");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AutoStart] 自启动流程发生异常");
            }
        });

        // 启动实例监控
        Task.Run(async () =>
        {
            await Task.Delay(5000);

            // 启动资源监控循环
            _logger.LogInformation("[Monitor] 正在启动服务器资源监控服务...");
            await StartResourceMonitoring();
        });
    }

    // 监听服务器退出 执行崩溃重启等内容
    private void HandleServerExit(uint instanceId, int exitCode)
    {
        if (!_activeServers.TryGetValue(instanceId, out var context)) return;

        // 用户手动停止
        if (context.IsStopping)
        {
            _activeServers.TryRemove(instanceId, out _);
            RecordLog(instanceId, context, "[MSLX] 服务器已停止 (用户操作)。");
            return;
        }

        // 不是从控制台停止的
        if (_activeServers.TryRemove(instanceId, out var removedContext))
        {
            string exitMsg = $"[MSLX] 服务器进程已停止，退出代码: {exitCode}";
            exitMsg += exitCode != 0 ? " (异常退出)" : " (正常关闭)";
            RecordLog(instanceId, removedContext, exitMsg);

            _logger.LogInformation($"MC 服务器 [{instanceId}] 停止处理完成 (Code: {exitCode})");

            // 自动重启
            try
            {
                var serverInfo = ConfigServices.ServerList.GetServer(instanceId);

                if (serverInfo != null && serverInfo.AutoRestart && (exitCode != 0 || serverInfo.ForceAutoRestart))
                {
                    // 熔断检查
                    var history = _crashHistory.GetOrAdd(instanceId, new List<DateTime>());

                    // 加锁处理 List
                    lock (history)
                    {
                        DateTime now = DateTime.Now;
                        history.Add(now); // 记录本次崩溃时间

                        // 清理超出时间窗口的旧记录
                        history.RemoveAll(t => t < now.AddSeconds(-CrashCheckWindowSeconds));

                        // 检查剩余的记录数量是否超过阈值
                        if (history.Count > MaxCrashCount)
                        {
                            RecordLog(instanceId, removedContext,
                                $">>> [MSLX] 严重错误：服务器在 {CrashCheckWindowSeconds} 秒内已崩溃 {history.Count} 次！");
                            RecordLog(instanceId, removedContext,
                                ">>> [MSLX] 为防止无限重启导致系统卡死，守护进程已放弃自动重启该实例。");
                            RecordLog(instanceId, removedContext,
                                ">>> [MSLX] 请检查服务器配置、Java环境或日志文件，修复问题后请手动启动。");

                            _logger.LogError($"实例 {instanceId} 触发重启熔断保护，停止重启。");

                            // 没救了喵
                            return;
                        }

                        RecordLog(instanceId, removedContext,
                            $">>> [MSLX] 检测到异常退出，正在准备第 {history.Count} 次尝试重启 (阈值: {MaxCrashCount}次/5分钟)...");
                    }

                    _ = Task.Run(async () =>
                    {
                        // 等5秒是好习惯
                        await Task.Delay(5000);

                        // 重新启动
                        var (success, msg) = StartServer(instanceId);
                        if (!success)
                        {
                            _logger.LogWarning($"[AutoRestart] 实例 {instanceId} 重启失败: {msg}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[AutoRestart] 自动重启逻辑出错");
            }
        }
    }

    /// <summary>
    /// 获取服务器已运行的时间
    /// </summary>
    public TimeSpan GetServerUptime(uint instanceId)
    {
        if (_activeServers.TryGetValue(instanceId, out var context))
        {
            if (context.Process != null && !context.IsInitializing && !context.Process.HasExited)
            {
                try
                {
                    return DateTime.Now - context.Process.StartTime;
                }
                catch
                {
                    return TimeSpan.Zero;
                }
            }
        }

        return TimeSpan.Zero;
    }

    /// <summary>
    /// 记录日志
    /// </summary>
    private void RecordLog(uint instanceId, ServerContext context, string? data)
    {
        if (string.IsNullOrWhiteSpace(data)) return;

        context.Logs.Enqueue(data);
        while (context.Logs.Count > MaxLogLines)
            context.Logs.TryDequeue(out _);

        // 通过 SignalR 推送日志
        _hubContext.Clients.Group(instanceId.ToString()).SendAsync("ReceiveLog", data);
    }

    // 安装Authlib-Injector
    private async Task<bool> DownloadAuthlib(string basePath, uint instanceId, ServerContext context)
    {
        try
        {
            HttpService.HttpResponse response =
                await GeneralApi.GetAsync("https://authlib-injector.mirrors.mslmc.cn/artifact/latest.json");
            if (response.IsSuccessStatusCode)
            {
                JObject authlibJobj = JObject.Parse(response.Content);
                if (!File.Exists(Path.Combine(basePath, "authlib-injector.jar")) ||
                    !await FileUtils.ValidateFileSha256Async(Path.Combine(basePath, "authlib-injector.jar"),
                        authlibJobj["checksums"]["sha256"].ToString()))
                {
                    // 下崽
                    var downloader = new ParallelDownloader(parallelCount: 1);
                    var (success, errorMsg) = await downloader.DownloadFileAsync(
                        authlibJobj["download_url"].ToString().Replace("authlib-injector.yushi.moe",
                            "authlib-injector.mirrors.mslmc.cn"), Path.Combine(basePath, "authlib-injector.jar"),
                        // 进度回调
                        async (progress, speed) =>
                        {
                            RecordLog(instanceId, context,
                                $"正在下载 Authlib-Injector... 进度: {progress:0.00}% | 下载速度: {speed}");
                        }
                    );

                    if (!success)
                    {
                        _logger.LogError("下载Authlib-Injector失败: {0}", errorMsg);
                        if (!File.Exists(Path.Combine(basePath, "authlib-injector.jar")))
                        {
                            return false;
                        }
                    }
                }
            }
            else
            {
                if (!File.Exists(Path.Combine(basePath, "authlib-injector.jar")))
                {
                    _logger.LogError("下载Authlib-Injector失败: 无法获取元数据。");
                    return false;
                }

                _logger.LogWarning("获取元数据失败，将使用旧版本Authlib-Injector。");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "下载Authlib-Injector失败");
            if (!File.Exists(Path.Combine(basePath, "authlib-injector.jar")))
            {
                return false;
            }
        }

        return true;
    }

    // —————— 备份相关 ——————
    public bool StartBackupServer(uint instanceId)
    {
        if (_activeServers.TryGetValue(instanceId, out var context))
        {
            _ = Task.Run(async () => await BackupServer(instanceId, context));
            return true;
        }

        return false;
    }

    private async Task BackupServer(uint instanceId, ServerContext context)
    {
        if (context.IsBackuping)
        {
            _logger.LogWarning($"[Backup] 忽略备份请求，实例 {instanceId} 正在备份中。");
            RecordLog(instanceId, context, "[MSLX-Backup] 正在备份中，请勿重复操作。");
            return;
        }

        try
        {
            context.IsBackuping = true;
            var server = ConfigServices.ServerList.GetServer(instanceId);
            if (server == null) return;

            if (IsServerRunning(instanceId))
            {
                SendCommand(instanceId, "save-off");
                await Task.Delay(1000);
                SendCommand(instanceId, "save-all");
                SendCommand(instanceId,
                    "tellraw @a [{\"text\":\"[\",\"color\":\"yellow\"},{\"text\":\"MSLX\",\"color\":\"green\"},{\"text\":\"]\",\"color\":\"yellow\"},{\"text\":\"正在进行服务器存档备份，请勿关闭服务器哦，否则可能造成回档！备份期间不会影响正常游戏~\",\"color\":\"aqua\"}]");
                RecordLog(instanceId, context, "[MSLX-Backup] 正在备份服务器存档...");

                await Task.Delay(server.BackupDelay * 1000); // 等待延迟时间进行保存
            }

            // 获取需要备份的内容
            string worldPath = "world";
            if (File.Exists(Path.Combine(server.Base, "server.properties")))
            {
                dynamic config = ServerPropertiesLoader.Load(Path.Combine(server.Base, "server.properties"),
                    Encoding.GetEncoding(server.FileEncoding));
                worldPath = config.level_name == "未知" ? "world" : config.level_name;
            }

            // 兼容插件端的文件夹分离模式
            string fullWorldPath = Path.Combine(server.Base, worldPath);
            string fullNetherPath = Path.Combine(server.Base, worldPath + "_nether");
            string fullEndPath = Path.Combine(server.Base, worldPath + "_the_end");

            // 备份列表
            var foldersToCompress = new List<string>();

            if (Directory.Exists(fullWorldPath)) foldersToCompress.Add(fullWorldPath);
            if (Directory.Exists(fullNetherPath)) foldersToCompress.Add(fullNetherPath);
            if (Directory.Exists(fullEndPath)) foldersToCompress.Add(fullEndPath);

            // 确保有文件夹需要备份
            if (foldersToCompress.Count == 0)
            {
                _logger.LogWarning("未找到任何世界存档文件夹（包括主世界、下界、末地），备份失败！");
                if (IsServerRunning(instanceId))
                {
                    SendCommand(instanceId, "save-on");
                    SendCommand(instanceId,
                        "tellraw @a [{\"text\":\"[\",\"color\":\"yellow\"},{\"text\":\"MSLX\",\"color\":\"green\"},{\"text\":\"]\",\"color\":\"yellow\"},{\"text\":\"备份失败！未找到任何世界存档文件夹！\",\"color\":\"red\"}]");
                }

                return;
            }

            // 拼接备份的目标保存位置
            string backupDir = Path.Combine(server.Base, "mslx-backups"); // 默认是存档内
            if (server.BackupPath != "MSLX://Backup/Instance")
            {
                if (server.BackupPath == "MSLX://Backup/Data")
                {
                    backupDir = Path.Combine(ConfigServices.GetAppDataPath(), "DaemonData", "Backups",
                        $"Backups_{server.Name}_{instanceId}");
                }
                else if (!string.IsNullOrEmpty(server.BackupPath))
                {
                    backupDir = Path.Combine(server.BackupPath);
                }
            }

            string backupPath = Path.Combine(backupDir, $"mslx-backup_{DateTime.Now.ToString("yyyyMMdd_HHmmss")}.zip");
            if (!Directory.Exists(backupDir)) Directory.CreateDirectory(backupDir);

            // 最大备份存档限制
            int maxBackups = 20;
            if (server.BackupMaxCount > 0) maxBackups = server.BackupMaxCount;

            // 删除多余的备份
            try
            {
                var backupFiles = Directory.GetFiles(backupDir, "mslx-backup_*.zip")
                    .Select(path => new FileInfo(path))
                    .OrderBy(fi => fi.Name) // 按文件名排序，文件名早的=时间旧的
                    .ToList();

                if (maxBackups >= 1 && backupFiles.Count >= maxBackups)
                {
                    int filesToDeleteCount = backupFiles.Count - maxBackups + 1;
                    var filesToDelete = backupFiles.Take(filesToDeleteCount).ToList();

                    // 遍历删除最旧的文件
                    foreach (var fileToDelete in filesToDelete)
                    {
                        try
                        {
                            fileToDelete.Delete();
                            RecordLog(instanceId, context, $"[MSLX-Backup] 已删除旧备份：{fileToDelete.Name}");
                        }
                        catch (Exception ex)
                        {
                            // 如果删除失败，仅发出警告，不中断整个备份过程
                            RecordLog(instanceId, context, $"[MSLX-Backup] 删除旧备份 {fileToDelete.Name} 失败：{ex.Message}");
                            _logger.LogWarning($"删除旧备份 {fileToDelete.Name} 失败：{ex.ToString()}");
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError($"删除多余的备份失败 {instanceId}, {e.Message}");
                RecordLog(instanceId, context, $"[MSLX-Backup] 删除多余的备份失败：{e.Message}");
            }

            // 开始压缩
            RecordLog(instanceId, context, $"[MSLX-Backup] 正在压缩服务器存档...");
            await using (FileStream zipToOpen = new FileStream(backupPath, FileMode.Create))
            {
                using (ZipArchive archive = new ZipArchive(zipToOpen, ZipArchiveMode.Create))
                {
                    foreach (var folderPath in foldersToCompress)
                    {
                        // 开始递归压缩
                        await CompressFolder(server.Base, folderPath, archive);
                    }
                }
            }

            // 输出备份信息
            if (IsServerRunning(instanceId))
            {
                try
                {
                    FileInfo backupFileInfo = new FileInfo(backupPath);
                    string fileName = backupFileInfo.Name;
                    long fileSizeInBytes = backupFileInfo.Length;
                    string formattedSize;
                    if (fileSizeInBytes > 1024 * 1024 * 1024)
                    {
                        formattedSize = $"{fileSizeInBytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
                    }
                    else if (fileSizeInBytes > 1024 * 1024)
                    {
                        formattedSize = $"{fileSizeInBytes / (1024.0 * 1024.0):F2} MB";
                    }
                    else if (fileSizeInBytes > 1024)
                    {
                        formattedSize = $"{fileSizeInBytes / 1024.0:F2} KB";
                    }
                    else
                    {
                        formattedSize = $"{fileSizeInBytes} Bytes";
                    }

                    string tellrawMessage = $"tellraw @a [";
                    tellrawMessage += "{\"text\":\"[\",\"color\":\"yellow\"},";
                    tellrawMessage += "{\"text\":\"MSLX\",\"color\":\"green\"},";
                    tellrawMessage += "{\"text\":\"]\",\"color\":\"yellow\"},";
                    tellrawMessage += "{\"text\":\" 服务器存档备份完成！\\n\",\"color\":\"aqua\"},";
                    tellrawMessage += $"{{\"text\":\"文件名: \",\"color\":\"gray\"}},";
                    tellrawMessage += $"{{\"text\":\"{fileName}\",\"color\":\"white\"}},";
                    tellrawMessage += $"{{\"text\":\"\\n大小: \",\"color\":\"gray\"}},";
                    tellrawMessage += $"{{\"text\":\"{formattedSize}\",\"color\":\"white\"}}";
                    tellrawMessage += "]";
                    SendCommand(instanceId, "save-on");
                    SendCommand(instanceId, tellrawMessage);
                }
                catch (Exception ex)
                {
                    RecordLog(instanceId, context, "[MSL备份] 无法获取备份文件信息：" + ex.Message);
                    _logger.LogWarning("无法获取备份文件信息：" + ex.ToString());
                    SendCommand(instanceId, "save-on");
                    SendCommand(instanceId,
                        "tellraw @a [{\"text\":\"[\",\"color\":\"yellow\"},{\"text\":\"MSLX\",\"color\":\"green\"},{\"text\":\"]\",\"color\":\"yellow\"},{\"text\":\"服务器存档备份完成！\",\"color\":\"aqua\"}]");
                }
            }

            RecordLog(instanceId, context, $"[MSLX-Backup] 存档备份成功！已保存至：{backupPath}");
            _logger.LogInformation($"[MSLX-Backup] 存档备份成功！已保存至：{backupPath}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"备份服务器失败 {instanceId}, {ex.Message}");
        }
        finally
        {
            context.IsBackuping = false;
            // 最后这里再执行一次 不知道有啥意义 留着吧 qwq
            if (IsServerRunning(instanceId))
            {
                SendCommand(instanceId, "save-on");
            }
        }
    }

    // 递归压缩方法
    private async Task CompressFolder(string rootPath, string currentPath, ZipArchive archive)
    {
        string[] files = Directory.GetFiles(currentPath);

        foreach (string file in files)
        {
            // 排除 session.lock
            if (Path.GetFileName(file).Equals("session.lock", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // 计算相对路径 (作为压缩包内的文件名)
            string entryName = Path.GetRelativePath(rootPath, file);

            try
            {
                // 共享只读打开
                await using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    // 在压缩包中创建条目
                    ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);

                    // 最后修改时间
                    entry.LastWriteTime = File.GetLastWriteTime(file);

                    // 文件流复制到压缩包条目流中
                    using (Stream entryStream = entry.Open())
                    {
                        await fs.CopyToAsync(entryStream);
                    }
                }
            }
            catch (IOException ex)
            {
                throw new IOException($"无法以共享只读模式打开文件 '{entryName}'。服务器施加了排他锁。错误: {ex.Message}", ex);
            }
        }

        // 递归处理子文件夹
        string[] folders = Directory.GetDirectories(currentPath);
        foreach (string folder in folders)
        {
            await CompressFolder(rootPath, folder, archive);
        }
    }

    // —————— 进程资源占用推送 ——————
    private async Task StartResourceMonitoring()
    {
        var timer = new PeriodicTimer(TimeSpan.FromSeconds(3)); 
        int processorCount = Environment.ProcessorCount;

        while (await timer.WaitForNextTickAsync(_appLifetime.ApplicationStopping))
        {
            if (_activeServers.IsEmpty) continue;

            foreach (var kvp in _activeServers)
            {
                var instanceId = kvp.Key;
                var context = kvp.Value;

                try
                {
                    if (context.Process == null || context.Process.HasExited || context.IsInitializing)
                    {
                        context.MonitorProcess = null;
                        continue;
                    }

                    // 确定监控目标
                    if (context.MonitorProcess == null || context.MonitorProcess.HasExited)
                    {
                        context.MonitorProcess = context.Process;
                    }

                    // [Windows] 穿透 cmd/conhost 查找 Java
                    if (OperatingSystem.IsWindows())
                    {
                        var name = context.MonitorProcess.ProcessName.ToLower();
                        if (name == "cmd" || name == "powershell" || name == "conhost" || name == "wt")
                        {
                            var child = GetChildJavaProcess(context.MonitorProcess.Id);
                            if (child != null) context.MonitorProcess = child;
                        }
                    }

                    // 刷新状态
                    var target = context.MonitorProcess;
                    target.Refresh();

                    if (target.HasExited) continue;

                    // 获取内存
                    long memoryUsage;
                    if (OperatingSystem.IsWindows())
                    {
                        memoryUsage = target.PrivateMemorySize64;
                    }
                    else
                    {
                        memoryUsage = target.WorkingSet64;
                    }

                    // 计算 CPU
                    double cpuUsage = 0;
                    var currentTime = DateTime.UtcNow;
                    var currentTotalProcessorTime = target.TotalProcessorTime;

                    // 只有当 PID 没变时才计算差值，防止进程切换瞬间 CPU 暴涨
                    if (context.PreviousCpuCheckTime != DateTime.MinValue && context.LastMonitoredPid == target.Id)
                    {
                        double timePassedMs = (currentTime - context.PreviousCpuCheckTime).TotalMilliseconds;
                        double cpuTimePassedMs = (currentTotalProcessorTime - context.PreviousTotalProcessorTime)
                            .TotalMilliseconds;

                        if (timePassedMs > 0)
                        {
                            cpuUsage = (cpuTimePassedMs / timePassedMs) / processorCount * 100;
                        }
                    }

                    // 更新上下文
                    context.PreviousCpuCheckTime = currentTime;
                    context.PreviousTotalProcessorTime = currentTotalProcessorTime;
                    context.LastMonitoredPid = target.Id;

                    if (cpuUsage > 100) cpuUsage = 100;
                    if (cpuUsage < 0) cpuUsage = 0;

                    await _hubContext.Clients.Group(instanceId.ToString()).SendAsync("ReceiveStatus",
                        instanceId,
                        Math.Round(cpuUsage, 2),
                        memoryUsage
                    );
                }
                catch
                {
                    context.PreviousCpuCheckTime = DateTime.MinValue;
                    context.MonitorProcess = null;
                }
            }
        }
    }

    /// <summary>
    /// [Windows专用] 通过 WMI 查找指定父进程启动的子进程 (Java/Bedrock)
    /// </summary>
    private Process? GetChildJavaProcess(int parentPid)
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            // 使用 WMI 查询：查找所有 ParentProcessId 等于当前 CMD PID 的进程
            using var searcher = new ManagementObjectSearcher(
                $"Select ProcessId, Name, CommandLine From Win32_Process Where ParentProcessId={parentPid}");

            using var collection = searcher.Get();

            foreach (var obj in collection)
            {
                var childPid = Convert.ToInt32(obj["ProcessId"]);
                var name = obj["Name"]?.ToString()?.ToLower() ?? "";

                //  Java 或 Bedrock 进程
                if (name.Contains("java") || name.Contains("bedrock") || name.Contains("server"))
                {
                    try
                    {
                        return Process.GetProcessById(childPid);
                    }
                    catch
                    {
                        // 进程可能刚查到就退出了，忽略
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"WMI 查询子进程失败: {ex.Message}");
        }

        return null;
    }
}