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
        /// 异步启动服务器
        /// </summary>
        private async Task InternalStartServerAsync(uint instanceId, ServerContext context, McServerInfo.ServerInfo serverInfo)
        {
            try
            {
                // 检查核心文件是否存在
                if (serverInfo.Core != "none" && serverInfo.Core.Contains("@libraries"))
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
                if (!File.Exists(serverInfo.Java) && serverInfo.Java != "java" && serverInfo.Java != "none")
                {
                    RecordLog(instanceId, context, $">>> [MSLX-MCServer] Java 路径无效: {serverInfo.Java}");
                    _activeServers.TryRemove(instanceId, out _);
                    return;
                }

                // 给予执行权限
                ExecutePermission.GrantExecutePermission(serverInfo.Base);
                await Task.Delay(100);

                string args = $"-jar {serverInfo.Core} {serverInfo.Args}";
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
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                var process = new Process { StartInfo = startInfo };

                // 绑定事件
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