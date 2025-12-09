using Microsoft.AspNetCore.SignalR;
using MSLX.Daemon.Hubs;
using MSLX.Daemon.Models;
using MSLX.Daemon.Utils;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace MSLX.Daemon.Services
{
    public class MCServerService
    {
        private readonly ILogger<MCServerService> _logger;
        private readonly IHubContext<InstanceConsoleHub> _hubContext;
        private readonly IHostApplicationLifetime _appLifetime;

        public class ServerContext
        {
            public Process? Process { get; set; }
            public ConcurrentQueue<string> Logs { get; set; } = new();
            public bool IsInitializing { get; set; } = false;
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
            if (string.IsNullOrWhiteSpace(encodingName)) return Encoding.UTF8;

            try
            {
                // 大小写不敏感，支持 "gbk", "utf-8", "big5" 等
                return Encoding.GetEncoding(encodingName);
            }
            catch (Exception)
            {
                // 如果编码名称无效，回退到 UTF-8
                _logger.LogWarning($"无法识别编码: {encodingName}，已回退到 UTF-8");
                return Encoding.UTF8;
            }
        }

        /// <summary>
        /// 异步启动服务器
        /// </summary>
        private async Task InternalStartServerAsync(uint instanceId, ServerContext context, McServerInfo.ServerInfo serverInfo)
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
                if (!File.Exists(serverInfo.Java) && serverInfo.Java != "java" && serverInfo.Java != "none" && !serverInfo.Java.StartsWith("MSLX://Java/"))
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
                        string eulaFileContent = $"#By changing the setting below to TRUE you are indicating your agreement to our EULA (https://aka.ms/MinecraftEULA).\n#{DateTime.Now}\neula=true";
                        await File.WriteAllTextAsync(eulaPath, eulaFileContent);

                        // 发送正式的提示信息
                        RecordLog(instanceId, context, ">>> [MSLX] 检测到 EULA 协议尚未签署，正在自动处理...");
                        RecordLog(instanceId, context, "=======================================================================");
                        RecordLog(instanceId, context, "                       Minecraft 最终用户许可协议声明                   ");
                        RecordLog(instanceId, context, "=======================================================================");
                        RecordLog(instanceId, context, "  原文提示: \"By changing the setting below to TRUE you are indicating");
                        RecordLog(instanceId, context, "  your agreement to our EULA (https://aka.ms/MinecraftEULA).\"");
                        RecordLog(instanceId, context, "");
                        RecordLog(instanceId, context, "  [重要提示]");
                        RecordLog(instanceId, context, "  为了启动服务器，系统已自动将 eula 设置为 true。");
                        RecordLog(instanceId, context, "  此操作即代表您已阅读、知悉并同意《Minecraft 最终用户许可协议》。");
                        RecordLog(instanceId, context, "  协议地址: https://aka.ms/MinecraftEULA");
                        RecordLog(instanceId, context, "=======================================================================");
                        
                        // 强制看5秒
                        RecordLog(instanceId, context, ">>> 协议签署完成，将在 5 秒后继续启动服务器...");
                        await Task.Delay(5000);
                    }
                }

                // 给予执行权限
                ExecutePermission.GrantExecutePermission(serverInfo.Base);
                await Task.Delay(100);

                string args = $"-Xms{serverInfo.MinM}M -Xmx{serverInfo.MaxM}M {serverInfo.Args} -jar {serverInfo.Core} nogui";
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
                    args = $"-Xms{serverInfo.MinM}M -Xmx{serverInfo.MaxM}M {serverInfo.Args} {serverInfo.Core} nogui";
                }

                // 处理编码
                Encoding inputEncoding = GetEncoding(serverInfo.InputEncoding);
                Encoding outputEncoding = GetEncoding(serverInfo.OutputEncoding);
                _logger.LogDebug($"实例 {instanceId} 编码设置 - 输入: {inputEncoding.EncodingName}, 输出: {outputEncoding.EncodingName}");

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
                    if (context.Process != null && !context.Process.HasExited)
                    {
                        try
                        {
                            context.Process.StandardInput.WriteLine("stop");
                            context.Process.StandardInput.Flush();

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
        public bool SendCommand(uint instanceId, string command)
        {
            if (_activeServers.TryGetValue(instanceId, out var context))
            {
                try
                {
                    if (context.Process != null && !context.Process.HasExited)
                    {
                        context.Process.StandardInput.WriteLine(command);
                        context.Process.StandardInput.Flush();
                        RecordLog(instanceId, context, $"[MSLX] 已发送命令: {command}");
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
        
        /// <summary>
        /// 统一处理服务器退出后的逻辑
        /// </summary>
        private void HandleServerExit(uint instanceId, int exitCode)
        {
            if (!_activeServers.TryGetValue(instanceId, out var context))
            {
                return;
            }
            
            if (_activeServers.TryRemove(instanceId, out var removedContext))
            {
                // 退出日志
                string exitMsg = $"[MSLX] 服务器进程已停止，退出代码: {exitCode}";
                if (exitCode != 0)
                {
                    exitMsg += " (异常退出)";
                }
                else
                {
                    exitMsg += " (正常关闭)";
                }
                
                RecordLog(instanceId, removedContext, exitMsg);
                _logger.LogInformation($"MC 服务器 [{instanceId}] 停止处理完成 (Code: {exitCode})");
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
    }
}