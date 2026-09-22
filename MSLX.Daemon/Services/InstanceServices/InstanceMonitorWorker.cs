using CliWrap;
using CliWrap.Buffered;
using Microsoft.AspNetCore.SignalR;
using MSLX.Daemon.Hubs;
using System.Diagnostics;
using System.Management;
using MSLX.Daemon.Utils;

namespace MSLX.Daemon.Services.InstanceServices;

/// <summary>
/// 实例资源监控后台服务：周期采集运行中实例的 CPU/内存占用（原生进程与 Docker 容器），
/// 并通过 SignalR 推送给前端。以 BackgroundService 形式随宿主启动（AddHostedService），
/// </summary>
public class InstanceMonitorWorker : BackgroundService
{
    private readonly ILogger<InstanceMonitorWorker> _logger;
    private readonly IHubContext<InstanceConsoleHub> _hubContext;
    private readonly InstanceStateStore _stateStore;

    public InstanceMonitorWorker(
        ILogger<InstanceMonitorWorker> logger,
        IHubContext<InstanceConsoleHub> hubContext,
        InstanceStateStore stateStore)
    {
        _logger = logger;
        _hubContext = hubContext;
        _stateStore = stateStore;
    }

    // —————— 进程资源占用推送 ——————
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[Monitor] 实例资源监控服务已启动");

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        int processorCount = Environment.ProcessorCount;

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    if (!_stateStore.HasAnyActive) continue;

                    // 批量查询Docker容器状态
                    var dockerInstanceIds = new List<uint>();
                    foreach (var kvp in _stateStore.ActiveEntries)
                    {
                        if (kvp.Value.IsDocker && !kvp.Value.IsInitializing)
                        {
                            dockerInstanceIds.Add(kvp.Key);
                        }
                    }

                    Dictionary<string, (double cpu, long memory)> dockerStatsMap = null!;
                    if (dockerInstanceIds.Count > 0)
                    {
                        dockerStatsMap = await GetBatchDockerStatsAsync(dockerInstanceIds, stoppingToken);
                    }

                    // 遍历推送
                    foreach (var kvp in _stateStore.ActiveEntries)
                    {
                        var instanceId = kvp.Key;
                        var context = kvp.Value;

                        try
                        {
                            if (context.IsDocker)
                            {
                                if (context.IsInitializing) continue;

                                string containerName = $"mslx-container-{instanceId}";
                                if (dockerStatsMap != null && dockerStatsMap.TryGetValue(containerName, out var stats))
                                {
                                    await _hubContext.Clients.Group(instanceId.ToString()).SendAsync("ReceiveStatus",
                                        instanceId,
                                        Math.Round(stats.cpu, 2),
                                        stats.memory
                                    );
                                }
                                continue;
                            }

                            // 原生宿主机实例
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

                            // 针对Windows / Linux系统的查询子进程
                            var name = context.MonitorProcess.ProcessName.ToLower();
                            if (OperatingSystem.IsWindows())
                            {
                                bool needFindChild = false;

                                if (name == "cmd" || name == "powershell" || name == "pwsh" || name == "conhost" ||
                                    name == "wt" || name == "python" || name == "python3" || name == "py")
                                {
                                    needFindChild = true;
                                }
                                else if (name == "java" || name == "javaw")
                                {
                                    try
                                    {
                                        string path = context.MonitorProcess.MainModule?.FileName?.ToLower() ?? "";
                                        if (path.Contains("javapath") || path.Contains("common files"))
                                        {
                                            needFindChild = true;
                                        }
                                    }
                                    catch { }
                                }

                                if (needFindChild)
                                {
                                    var child = GetChildJavaProcess(context.MonitorProcess.Id);
                                    if (child != null && child.Id != context.MonitorProcess.Id)
                                    {
                                        _logger.LogInformation($"[Monitor] 识别到 Wrapper 进程，切换监控目标: {context.MonitorProcess.Id} -> {child.Id}");
                                        context.MonitorProcess = child;
                                    }
                                }
                            }
                            else if (OperatingSystem.IsLinux())
                            {
                                if (name == "bash" || name == "sh" || name == "dash" ||
                                    name.StartsWith("python") || name == "py")
                                {
                                    var child = GetChildProcessLinux(context.MonitorProcess.Id);
                                    if (child != null && child.Id != context.MonitorProcess.Id)
                                    {
                                        _logger.LogInformation($"[Monitor] Linux: 识别到 Shell Wrapper 进程，切换监控目标: {context.MonitorProcess.Id} -> {child.Id}");
                                        context.MonitorProcess = child;
                                    }
                                }
                            }

                            // 刷新状态
                            var target = context.MonitorProcess;
                            target.Refresh();

                            if (target.HasExited) continue;

                            // 获取内存
                            long memoryUsage = OperatingSystem.IsWindows() ? target.PrivateMemorySize64 : target.WorkingSet64;

                            // 计算 CPU
                            double cpuUsage = 0;
                            var currentTime = DateTime.UtcNow;
                            var currentTotalProcessorTime = target.TotalProcessorTime;

                            if (context.PreviousCpuCheckTime != DateTime.MinValue && context.LastMonitoredPid == target.Id)
                            {
                                double timePassedMs = (currentTime - context.PreviousCpuCheckTime).TotalMilliseconds;
                                double cpuTimePassedMs = (currentTotalProcessorTime - context.PreviousTotalProcessorTime).TotalMilliseconds;

                                cpuUsage = InstanceMetricsUtils.ComputeCpuPercent(cpuTimePassedMs, timePassedMs, processorCount);
                            }

                            context.PreviousCpuCheckTime = currentTime;
                            context.PreviousTotalProcessorTime = currentTotalProcessorTime;
                            context.LastMonitoredPid = target.Id;

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
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // 降级继续：单次采集失败不终止监控循环，也不会触发宿主 StopHost
                    _logger.LogError(ex, "[Monitor] 单次采集发生异常，已跳过并继续监控");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 宿主关停，正常退出
        }

        _logger.LogInformation("[Monitor] 实例资源监控服务已停止");
    }

    /// <summary>
    /// 批量获取所有活动 Docker 容器的 CPU 与内存指标 (按配置核心数折算，最高 100%)
    /// </summary>
    private async Task<Dictionary<string, (double cpu, long memory)>> GetBatchDockerStatsAsync(List<uint> dockerInstanceIds, CancellationToken stoppingToken)
    {
        var statsMap = new Dictionary<string, (double cpu, long memory)>();
        if (dockerInstanceIds == null || dockerInstanceIds.Count == 0) return statsMap;

        try
        {
            var containerNames = dockerInstanceIds.Select(id => $"mslx-container-{id}").ToList();
            var args = new List<string> { "stats", "--no-stream", "--format", "{{.Name}}|{{.CPUPerc}}|{{.MemUsage}}" };
            args.AddRange(containerNames);

            var result = await Cli.Wrap("docker")
                .WithArguments(args)
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync(stoppingToken);

            if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
                return statsMap;

            var lines = result.StandardOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                // 输出格式: "mslx-container-1|150.35%|345.2MiB / 8GiB"
                var parsed = InstanceMetricsUtils.ParseDockerStatsLine(line);
                if (parsed == null) continue;

                var (instanceId, containerName, rawCpu, memoryBytes) = parsed.Value;

                // 优先从内存 ServerContext 获取预计算好的 CPU 基准
                double baseLimitPercentage = 0;
                if (_stateStore.Get(instanceId) is { } context)
                {
                    baseLimitPercentage = context.CpuBaseLimitPercentage;
                }

                // 回退宿主机核心数
                if (baseLimitPercentage <= 0)
                {
                    baseLimitPercentage = Environment.ProcessorCount * 100.0;
                }

                // 计算相对占用，封顶 100%
                double normalizedCpu = InstanceMetricsUtils.NormalizeDockerCpu(rawCpu, baseLimitPercentage);

                statsMap[containerName] = (normalizedCpu, memoryBytes);
            }
        }
        catch (OperationCanceledException)
        {
            throw; // 关停取消不视为失败，向上传递由 ExecuteAsync 统一处理
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"[Monitor] 批量提取 Docker 容器状态失败: {ex.Message}");
        }

        return statsMap;
    }

    /// <summary>
    /// [Linux专用] 通过 pgrep 递归查找指定父进程启动的服务端子进程
    /// 可穿透 shell / Python(MCDR) 等中间包装进程
    /// </summary>
    private Process? GetChildProcessLinux(int parentPid, int depth = 0)
    {
        if (!OperatingSystem.IsLinux()) return null;
        if (depth > 8) return null; // 防御性深度限制，避免异常情况下无限递归

        try
        {
            // 使用 pgrep 查找直接子进程
            var startInfo = new ProcessStartInfo
            {
                FileName = "pgrep",
                Arguments = $"-P {parentPid}",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return null;

            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(1000);

            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (int.TryParse(line.Trim(), out int childPid))
                {
                    try
                    {
                        var childProcess = Process.GetProcessById(childPid);
                        var name = childProcess.ProcessName.ToLower();

                        // 找到了目标服务端进程
                        if (name.Contains("java") || name.Contains("bedrock") || name.Contains("server"))
                        {
                            return childProcess;
                        }

                        // 如果子进程依然是 shell / Python 包装器，递归往下挖
                        if (name == "bash" || name == "sh" || name == "dash" ||
                            name.StartsWith("python") || name == "py")
                        {
                            var grandChild = GetChildProcessLinux(childPid, depth + 1);
                            if (grandChild != null) return grandChild;
                        }
                    }
                    catch
                    {
                        // 进程可能瞬间退出了，忽略即可
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Linux 查询子进程失败: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// [Windows专用] 通过 WMI 递归查找指定父进程启动的服务端子进程 (Java/Bedrock)
    /// 可穿透 cmd / Python(MCDR) / PowerShell 等中间包装进程
    /// </summary>
    private Process? GetChildJavaProcess(int parentPid, int depth = 0)
    {
        if (!OperatingSystem.IsWindows()) return null;
        if (depth > 8) return null; // 防御性深度限制，避免异常情况下无限递归

        try
        {
            // 使用 WMI 查询：查找所有 ParentProcessId 等于当前 PID 的进程
            using var searcher = new ManagementObjectSearcher(
                $"Select ProcessId, Name, CommandLine From Win32_Process Where ParentProcessId={parentPid}");

            using var collection = searcher.Get();

            foreach (var obj in collection)
            {
                var childPid = Convert.ToInt32(obj["ProcessId"]);
                var name = obj["Name"]?.ToString()?.ToLower() ?? "";

                //  Java 或 Bedrock 进程 —— 找到目标
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

                // 中间包装进程(cmd / Python(MCDR) / PowerShell 等)，继续向下递归
                if (name.Contains("python") || name == "py.exe" || name == "cmd.exe" ||
                    name.Contains("powershell") || name == "pwsh.exe" || name == "conhost.exe")
                {
                    var grandChild = GetChildJavaProcess(childPid, depth + 1);
                    if (grandChild != null) return grandChild;
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
