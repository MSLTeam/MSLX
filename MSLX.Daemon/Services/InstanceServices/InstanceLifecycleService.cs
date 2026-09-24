using Microsoft.AspNetCore.SignalR;
using MSLX.Daemon.Hubs;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.Events;
using MSLX.SDK.Interfaces;
using MSLX.SDK.IServices;
using System.Diagnostics;
using System.Text;

namespace MSLX.Daemon.Services.InstanceServices;

public class InstanceLifecycleService : IInstanceLifecycleService
{

    private readonly ILogger<IInstanceLifecycleService> _logger;
    private readonly IHubContext<InstanceConsoleHub> _hubContext;
    private readonly IHostApplicationLifetime _appLifetime;
    private readonly IFrpProcessService _frpService;
    private readonly IMSLXEvents _events;
    private readonly InstanceStateStore _stateStore;
    private readonly InstanceConsoleService _console;
    private readonly IServiceProvider _serviceProvider;
    private readonly InstanceLauncherService _launcher;
    private readonly MSLX.SDK.IServices.IInstanceConsoleService _instanceConsole;

    // 短时间内崩溃重启限制（300 秒内最多崩溃 5 次，超过则熔断放弃自动重启）
    private readonly CrashRestartGuard _crashGuard = new(windowSeconds: 300, maxCount: 5);

    public InstanceLifecycleService(
        ILogger<IInstanceLifecycleService> logger,
        IHubContext<InstanceConsoleHub> hubContext,
        IHostApplicationLifetime appLifetime,
        IFrpProcessService frpService,
        IMSLXEvents events,
        InstanceStateStore stateStore,
        InstanceConsoleService console,
        IServiceProvider serviceProvider, InstanceLauncherService launcher, MSLX.SDK.IServices.IInstanceConsoleService instanceConsole)
    {
        _logger = logger;
        _hubContext = hubContext;
        _appLifetime = appLifetime;
        _frpService = frpService;
        _events = events;
        _stateStore = stateStore;
        _console = console;
        _serviceProvider = serviceProvider;
        _launcher = launcher;
        _instanceConsole = instanceConsole;

        _appLifetime.ApplicationStopping.Register(StopAllServers);
        _appLifetime.ApplicationStarted.Register(OnAppStarted);

        // 注册编码提供程序，以支持 GBK 等非 Unicode 编码
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    #region 基础守护进程

    /// <summary>
    /// 检查服务器是否正在运行
    /// </summary>
    public bool IsServerRunning(uint instanceId)
    {
        if (_stateStore.Get(instanceId) is { } context)
        {
            if (context.IsInitializing) return true;

            lock (context.StateLock)
            {
                bool isRunning = context.IsPtyMode
                    ? !context.IsProcessExited
                    : (!context.IsProcessExited || !context.IsStdoutClosed || !context.IsStderrClosed);

                if (isRunning)
                {
                    return true;
                }
            }

            _stateStore.Remove(instanceId);
        }

        return false;
    }

    /// <summary>
    /// 获取服务器详细状态
    /// 0:未启动, 1:启动中, 2:运行中, 3:停止中, 4:重启中
    /// </summary>
    public (int status, string description) GetServerStatus(uint instanceId)
    {
        if (_stateStore.IsRestarting(instanceId))
        {
            return (4, "重启中");
        }

        if (_stateStore.Get(instanceId) is { } context)
        {
            if (context.IsStopping)
            {
                return (3, "停止中");
            }

            if (context.IsInitializing)
            {
                return (1, "启动中");
            }

            if (context.Process != null)
            {
                lock (context.StateLock)
                {
                    bool isRunning = context.IsPtyMode
                        ? !context.IsProcessExited
                        : (!context.IsProcessExited || !context.IsStdoutClosed || !context.IsStderrClosed);

                    if (isRunning)
                    {
                        return (2, "运行中");
                    }
                }
            }
        }

        return (0, "未启动");
    }

    /// <summary>
    /// 检查是否有任何服务器实例处于活动状态
    /// </summary>
    public bool HasRunningServers()
    {
        return _stateStore.HasAnyActive;
    }


    /// <summary>
    /// 获取指定实例当前的在线玩家列表
    /// </summary>
    public List<string> GetOnlinePlayers(uint instanceId)
    {
        if (_stateStore.Get(instanceId) is { } context)
        {
            return context.OnlinePlayers.Keys.ToList();
        }

        return new List<string>();
    }


    /// <summary>
    /// 启动 MC 服务器 (非阻塞模式)
    /// </summary>
        public (bool success, string message) StartServer(uint instanceId,
        bool isAutoRestart = false, bool skipEulaCheck = false)
    {
        if (IsServerRunning(instanceId))
            return (false, "该服务器已经在运行中或正在启动中");

        if (!isAutoRestart)
        {
            _crashGuard.Reset(instanceId);
        }

        var serverInfo = IConfigBase.ServerList.GetServer(instanceId);
        if (serverInfo == null)
            return (false, "找不到指定的服务器配置");

        var context = new ServerContext { IsInitializing = true };
        _stateStore.Set(instanceId, context);

        _ = Task.Run(async () => await _launcher.LaunchAsync(instanceId, context, serverInfo, skipEulaCheck, isAutoRestart, HandleServerExit));

        return (true, "正在启动服务器...");
    }

    public Task<bool> AgreeEULA(uint instanseId, bool agree) => _launcher.AgreeEULA(instanseId, agree);
    /// <summary>
    /// 停止服务器
    /// </summary>
    public bool StopServer(uint instanceId)
    {
        if (_stateStore.Get(instanceId) is { } context)
        {
            try
            {
                context.IsStopping = true;
                _logger.LogInformation($"正在准备停止服务端实例: {instanceId}");

                _ = Task.Run(() =>
                {
                    try
                    {
                        if (context.Process != null)
                        {
                            bool isSchrodingerState = false;
                            bool isCompletelyDead = false;

                            lock (context.StateLock)
                            {
                                if (context.IsPtyMode)
                                {
                                    isCompletelyDead = context.IsProcessExited;
                                    isSchrodingerState = false;
                                }
                                else
                                {
                                    // 子进程还在
                                    isSchrodingerState = context.IsProcessExited &&
                                                         (!context.IsStdoutClosed || !context.IsStderrClosed);
                                    // 全关掉了
                                    isCompletelyDead = context.IsProcessExited && context.IsStdoutClosed &&
                                                       context.IsStderrClosed;
                                }
                            }

                            if (isCompletelyDead)
                            {
                                // 全关掉了
                                return;
                            }

                            var server = IConfigBase.ServerList.GetServer(instanceId);
                            int waitTimeMs = (server?.ForceExitDelay ?? 10) * 1000;

                            bool isDockerMode = server != null && (server.Java == "docker-java" || server.Java == "docker-custom");

                            try
                            {
                                if (isSchrodingerState)
                                {
                                    // 没有StdIn 只能kill了
                                    _console.RecordLog(instanceId, context,
                                        ">>> [MSLX-Daemon] 服务端处于特殊接管状态，无法发送安全停止命令，正在强制结束进程...");

                                    if (isDockerMode)
                                    {
                                        Process.Start(new ProcessStartInfo
                                        {
                                            FileName = "docker",
                                            Arguments = $"rm -f mslx-container-{instanceId}",
                                            CreateNoWindow = true,
                                            UseShellExecute = false
                                        })?.WaitForExit(3000);
                                    }

                                    context.Process.Kill(true);
                                }
                                else
                                {
                                    string stopCmdForEvent = string.IsNullOrEmpty(server?.StopCommand) ? "stop" : server.StopCommand;
                                    _events.PublishServerStopping(new ServerStoppingEventArgs
                                    {
                                        InstanceId = instanceId,
                                        ServerInfo = server,
                                        StopCommand = stopCmdForEvent,
                                        Timestamp = DateTime.Now
                                    });

                                    // 判定是否是传统MC服务器或者是开启了docker-java包装的MC服务器
                                    bool isMcServer = server != null && server.Java != "none" && server.Java != "docker-custom";

                                    if (isMcServer)
                                    {
                                        // MC服务器：发送 stop / 自定义 命令
                                        string stopCmd = string.IsNullOrEmpty(server?.StopCommand) ? "stop" : server.StopCommand;
                                        _console.RecordLog(instanceId, context, $">>> [MSLX-Daemon] 准备执行停止指令: {stopCmd}");
                                        _instanceConsole.SendCommand(instanceId, stopCmd, true);
                                        _console.RecordLog(instanceId, context, "[MSLX] 已发送关闭指令，正在等待服务退出...");
                                    }
                                    else
                                    {
                                        // 其他类型
                                        if (string.IsNullOrEmpty(server?.StopCommand ?? "") ||
                                            (server?.StopCommand ?? "") == "^c")
                                        {
                                            _console.RecordLog(instanceId, context, ">>> [MSLX-Daemon] 准备发送中断信号 (^C)...");
                                            if (context.IsPtyMode && context.PtyConnection != null)
                                            {
                                                try { context.PtyConnection.WriterStream.Write(new byte[] { 0x03 }, 0, 1); } catch { }
                                            }
                                            else
                                            {
                                                ProcessHelper.SendCtrlC(context.Process);
                                            }
                                            _console.RecordLog(instanceId, context, "[MSLX] 已发送中断信号，正在等待服务退出...");
                                        }
                                        else
                                        {
                                            string stopCmd = server?.StopCommand ?? "stop";
                                            _console.RecordLog(instanceId, context, $">>> [MSLX-Daemon] 准备执行停止指令: {stopCmd}");
                                            _instanceConsole.SendCommand(instanceId, stopCmd, true);
                                            _console.RecordLog(instanceId, context, "[MSLX] 已发送关闭指令，正在等待服务退出...");
                                        }

                                        // 关闭输入流
                                        if (!context.IsPtyMode && (!server?.Args?.ToLower().Contains("mcdreforged") ?? true))
                                        {
                                            try { context.Process.StandardInput.Close(); } catch { }
                                        }
                                    }

                                    // 等待进程退出
                                    if (!context.Process.WaitForExit(waitTimeMs))
                                    {
                                        // 超时未退出 -> 强制树形结束
                                        if (isDockerMode)
                                        {
                                            _logger.LogInformation($"[Docker-Guard] 实例 {instanceId} 关闭超时，正在强行清理容器...");
                                            Process.Start(new ProcessStartInfo
                                            {
                                                FileName = "docker",
                                                Arguments = $"rm -f mslx-container-{instanceId}",
                                                CreateNoWindow = true,
                                                UseShellExecute = false
                                            })?.WaitForExit(3000);
                                        }

                                        context.Process.Kill(true);
                                        _console.RecordLog(instanceId, context, "[MSLX] 服务器超时，已强制结束进程树");
                                        _logger.LogWarning($"服务器实例 {instanceId} 关闭超时，已强制结束进程树");
                                    }

                                    lock (context.StateLock)
                                    {
                                        context.IsProcessExited = true;
                                        try { context.FinalExitCode = context.Process.ExitCode; } catch { }
                                    }
                                    ForceTriggerExit(instanceId, context);
                                }
                            }
                            catch (Exception ex)
                            {
                                // 发生任何异常直接强杀
                                try
                                {
                                    if (isDockerMode)
                                    {
                                        Process.Start(new ProcessStartInfo
                                        {
                                            FileName = "docker",
                                            Arguments = $"rm -f mslx-container-{instanceId}",
                                            CreateNoWindow = true,
                                            UseShellExecute = false
                                        })?.WaitForExit(3000);
                                    }

                                    context.Process.Kill(true);
                                }
                                catch
                                {
                                }

                                _console.RecordLog(instanceId, context, $"[MSLX] 停止过程出错，已强制结束: {ex.Message}");
                                _logger.LogWarning($"服务器实例 {instanceId} 停止过程出错，已强制结束: {ex.Message}");

                                lock (context.StateLock)
                                {
                                    context.IsProcessExited = true;
                                    try { context.FinalExitCode = context.Process.ExitCode; } catch { }
                                }
                                ForceTriggerExit(instanceId, context);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"停止服务器 [{instanceId}] 后台任务异常");
                        ForceTriggerExit(instanceId, context);
                    }
                });

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"停止服务器 [{instanceId}] 时出错");
                _console.RecordLog(instanceId, context, $">>> [MSLX] 停止失败: {ex.Message}");
            }
        }

        return false;
    }

    /// <summary>
    /// 强制终止服务器进程
    /// </summary>
    public bool ForceKillServer(uint instanceId)
    {
        if (_stateStore.Get(instanceId) is { } context)
        {
            try
            {
                context.IsStopping = true;

                var serverInfo = IConfigBase.ServerList.GetServer(instanceId);
                if (serverInfo != null && (serverInfo.Java == "docker-java" || serverInfo.Java == "docker-custom"))
                {
                    _logger.LogInformation($"[Docker-Guard] 正在强行终结容器: mslx-container-{instanceId}");
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "docker",
                        Arguments = $"rm -f mslx-container-{instanceId}",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    })?.WaitForExit(3000);
                }

                if (context.PtyConnection != null)
                {
                    try { context.PtyConnection.Kill(); } catch { }
                }
                context.PtyReadCts?.Cancel();

                if (context.Process != null && !context.Process.HasExited)
                {
                    context.Process.Kill(true);
                    _console.RecordLog(instanceId, context, "[MSLX] 已强制结束进程及其子进程");
                    context.Process.WaitForExit(1000);
                }

                _stateStore.Remove(instanceId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"强制终止服务器 [{instanceId}] 时出错");
            }
        }
        return false;
    }

    /// <summary>
    /// 重启服务器 (停止 -> 等待 -> 启动)
    /// </summary>
    public async Task<(bool success, string message)> RestartServer(uint instanceId)
    {
        _stateStore.MarkRestarting(instanceId);
        _logger.LogInformation($"正在准备重启服务端实例: {instanceId}");

        try
        {
            // 如果服务器正在运行，先执行停止流程
            if (IsServerRunning(instanceId))
            {
                if (_stateStore.Get(instanceId) is { } context)
                {
                    _console.RecordLog(instanceId, context, "[MSLX] 正在执行重启...");
                }

                // 调用 StopServer
                bool stopTriggered = StopServer(instanceId);

                if (!stopTriggered)
                {
                    return (false, "重启失败：无法停止当前正在运行的服务器实例。");
                }

                var serverInfo = IConfigBase.ServerList.GetServer(instanceId);
                int maxWaitSeconds = (serverInfo?.ForceExitDelay ?? 30) + 5;
                int waitedMs = 0;
                int checkInterval = 500; // 每 0.5 秒检查一次

                // 只要服务器还在运行，就一直等
                while (IsServerRunning(instanceId))
                {
                    await Task.Delay(checkInterval);
                    waitedMs += checkInterval;

                    if (waitedMs >= maxWaitSeconds * 1000)
                    {
                        _logger.LogWarning($"重启实例 {instanceId} 失败：等待服务器停止超时 ({maxWaitSeconds}s)。请尝试手动强制结束。");
                        return (false, $"重启失败：等待服务器停止超时 ({maxWaitSeconds}s)。请尝试手动强制结束。");
                    }
                }

                // 停止后稍微缓冲一下，释放端口
                await Task.Delay(1000);
            }

            // 重新启动
            var result = StartServer(instanceId);

            if (result.success)
            {
                if (_stateStore.Get(instanceId) is { } newContext)
                {
                    _console.RecordLog(instanceId, newContext, "[MSLX] 正在重新启动实例...");
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"重启实例 {instanceId} 异常");
            return (false, $"重启流程发生异常: {ex.Message}");
        }
        finally
        {
            _stateStore.ClearRestarting(instanceId);
        }
    }

    /// <summary>
    /// 向服务器发送命令
    /// <summary>
    /// 获取服务器日志
    /// </summary>

    /// <summary>
    /// 获取 PTY 历史缓冲数据块
    /// </summary>

    // —————— 备份相关（已委托给 InstanceBackupService） ——————

    /// <summary>
    /// 停止所有服务器
    /// </summary>
    public void StopAllServers()
    {
        if (!_stateStore.HasAnyActive) return;

        _logger.LogInformation("正在停止所有 MC 服务器...");

        foreach (var kvp in _stateStore.ActiveEntries)
        {
            try
            {
                var context = kvp.Value;
                if (context.Process != null && !context.Process.HasExited)
                {
                    try
                    {
                        _instanceConsole.SendCommand(kvp.Key, "stop");
                        context.Process.WaitForExit(5000);
                    }
                    catch
                    {
                        context.Process.Kill(true);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"停止服务器 [{kvp.Key}] 时出错");
            }
        }

        _stateStore.ClearAll();
    }

    // 启动的生命周期事件
    private void OnAppStarted()
    {
        Task.Run(async () =>
        {
            try
            {
                // 等0.5s 确保服务全部初始化了
                // await Task.Delay(500);

                _logger.LogInformation("[AutoStart] 正在检查自启动实例...");
                var config = IConfigBase.ServerList.GetServerList();

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
    }

    // 监听服务器退出 执行崩溃重启等内容
    private void HandleServerExit(uint instanceId, ServerContext context, int exitCode)
    {
        _stateStore.Remove(instanceId);

        try
        {
            var serverInfo = IConfigBase.ServerList.GetServer(instanceId);
            if (serverInfo != null && !string.IsNullOrWhiteSpace(serverInfo.BindFrpId))
            {
                var frpIds = serverInfo.BindFrpId.Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (var idStr in frpIds)
                {
                    if (int.TryParse(idStr.Trim(), out int frpId))
                    {
                        _console.RecordLog(instanceId, context, $"[MSLX-Daemon] 服务端实例已退出，正在联动同步关闭隧道 [{frpId}]...");
                        _frpService.StopFrp(frpId);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[MSLX-Daemon] 实例 [{instanceId}] 联动关闭隧道时发生异常");
        }

        // 清空在线玩家
        context.OnlinePlayers.Clear();
        _hubContext.Clients.Group(instanceId.ToString()).SendAsync("PlayerListCleared", instanceId);

        _hubContext.Clients.Group("pty_" + instanceId).SendAsync("PtyStatus", new { isPty = context.IsPtyMode, isRunning = false });

        string exitMsg = $"[MSLX] 服务器进程已停止，退出代码: {exitCode}";
        if (context.IsStopping)
        {
            exitMsg += " (用户操作)";
        }
        else
        {
            exitMsg += exitCode != 0 ? " (异常退出)" : " (正常关闭)";
        }
        _console.RecordLog(instanceId, context, exitMsg);

        var serverInfoForExitEvent = IConfigBase.ServerList.GetServer(instanceId);
        TimeSpan uptime = TimeSpan.Zero;
        if (context.Process != null)
        {
            try { uptime = DateTime.Now - context.Process.StartTime; } catch { }
        }

        _events.PublishServerStopped(new ServerStoppedEventArgs
        {
            InstanceId = instanceId,
            ServerInfo = serverInfoForExitEvent,
            ExitCode = exitCode,
            Uptime = uptime,
            Timestamp = DateTime.Now
        });

        if (!context.IsStopping && exitCode != 0)
        {
            _events.PublishServerCrashed(new ServerCrashedEventArgs
            {
                InstanceId = instanceId,
                ServerInfo = serverInfoForExitEvent,
                ExitCode = exitCode,
                CrashMessage = exitMsg,
                Timestamp = DateTime.Now
            });
        }

        _logger.LogInformation($"MC 服务器 [{instanceId}] 停止处理完成 (Code: {exitCode})");

        // 用户主动停止，不触发崩溃自启
        if (context.IsStopping)
        {
            return;
        }

        // 自动重启
        try
            {
                var serverInfo = IConfigBase.ServerList.GetServer(instanceId);

                if (serverInfo != null && serverInfo.AutoRestart && (exitCode != 0 || serverInfo.ForceAutoRestart))
                {
                    // 熔断检查
                    int attempts = _crashGuard.RecordCrash(instanceId, DateTime.Now);

                    // 检查窗口内的崩溃次数是否超过阈值
                    if (attempts > _crashGuard.MaxCount)
                    {
                        _console.RecordLog(instanceId, context,
                            $">>> [MSLX] 严重错误：服务器在 {_crashGuard.WindowSeconds} 秒内已崩溃 {attempts} 次！");
                        _console.RecordLog(instanceId, context,
                            ">>> [MSLX] 为防止无限重启导致系统卡死，守护进程已放弃自动重启该实例。");
                        _console.RecordLog(instanceId, context,
                            ">>> [MSLX] 请检查服务器配置、Java环境或日志文件，修复问题后请手动启动。");

                        _logger.LogError($"实例 {instanceId} 触发重启熔断保护，停止重启。");

                        // 没救了喵
                        return;
                    }

                    _console.RecordLog(instanceId, context,
                        $">>> [MSLX] 检测到异常退出，正在准备第 {attempts} 次尝试重启 (阈值: {_crashGuard.MaxCount}次/5分钟)...");

                    _stateStore.MarkRestarting(instanceId); // 标记重启中

                    _ = Task.Run(async () =>
                    {
                        // 等5秒是好习惯
                        await Task.Delay(5000);

                        _stateStore.ClearRestarting(instanceId); // 移除重启标记

                        // 重新启动
                        var (success, msg) = StartServer(instanceId, true);
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

    /// <summary>
    /// 检查并处理真正的进程退出（标准流彻底不输出了喵！）
    public TimeSpan GetServerUptime(uint instanceId)
    {
        if (_stateStore.Get(instanceId) is { } context)
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


    private void ForceTriggerExit(uint instanceId, ServerContext context)
    {
        lock (context.StateLock)
        {
            if (context.HasTriggeredExit) return;
            context.HasTriggeredExit = true;
            
            if (context.IsPtyMode)
            {
                Task.Run(async () =>
                {
                    await Task.Delay(300);
                    try { context.PtyReadCts?.Cancel(); } catch { }
                    try { context.PtyConnection?.Dispose(); } catch { }
                });
            }
            HandleServerExit(instanceId, context, context.FinalExitCode);
        }
    }
    #endregion
}
