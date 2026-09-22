using CliWrap;
using CliWrap.Buffered;
using Microsoft.AspNetCore.SignalR;
using MSLX.Daemon.Hubs;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.SDK.Events;
using MSLX.SDK.Interfaces;
using MSLX.SDK.IServices;
using MSLX.SDK.Models;
using Newtonsoft.Json.Linq;
using Porta.Pty;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace MSLX.Daemon.Services.InstanceServices;

/// <summary>
/// 实例启动器服务：专门负责构造启动参数、预检环境、运行进程并挂载输出。
/// </summary>
public class InstanceLauncherService
{
    private readonly ILogger<InstanceLauncherService> _logger;
    private readonly IHubContext<InstanceConsoleHub> _hubContext;
    private readonly IMSLXEvents _events;
    private readonly InstanceStateStore _stateStore;
    private readonly InstanceConsoleService _console;
    private readonly IFrpProcessService _frpService;

    public InstanceLauncherService(
        ILogger<InstanceLauncherService> logger,
        IHubContext<InstanceConsoleHub> hubContext,
        IMSLXEvents events,
        InstanceStateStore stateStore,
        InstanceConsoleService console,
        IFrpProcessService frpService)
    {
        _logger = logger;
        _hubContext = hubContext;
        _events = events;
        _stateStore = stateStore;
        _console = console;
        _frpService = frpService;
    }

    public async Task<bool> AgreeEULA(uint instanseId, bool agree)
    {
        var serverInfo = IConfigBase.ServerList.GetServer(instanseId);
        if (serverInfo == null)
            return (false);
        if (agree)
        {
            string eulaPath = ServerPropertiesPathUtils.ResolveEulaPath(serverInfo);
            try
            {
                // 获取eula的位置
                var eulaDir = Path.GetDirectoryName(eulaPath);
                if (!string.IsNullOrEmpty(eulaDir))
                {
                    Directory.CreateDirectory(eulaDir);
                }

                // 写入同意后的文件内容
                string eulaFileContent =
                    $"#By changing the setting below to TRUE you are indicating your agreement to our EULA (https://aka.ms/MinecraftEULA).\n#{DateTime.Now}\neula=true";
                await File.WriteAllTextAsync(eulaPath, eulaFileContent);
            }
            catch
            {
                return false;
            }
        }

        // Server will be started by MCServerService via caller
        return true;
    }

    private static bool IsRunningInContainer()
    {
        var inContainer = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER");
        return inContainer != null && inContainer.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 逆向反查当前 Daemon 容器在宿主机上的真实物理数据根路径


    private async Task<string?> GetHostPhysicalDataPathAsync(uint instanceId, ServerContext context)
    {
        try
        {
            string containerId = Environment.MachineName.Trim();

            const string mountinfoPath = "/proc/self/mountinfo";
            if (File.Exists(mountinfoPath))
            {
                string mountinfo = await File.ReadAllTextAsync(mountinfoPath);
                var match = System.Text.RegularExpressions.Regex.Match(mountinfo, @"/docker/containers/([a-f0-9]{64})/");
                if (match.Success && match.Groups.Count > 1)
                {
                    containerId = match.Groups[1].Value;
                    _logger.LogInformation($"[Docker-Inspector] 从 mountinfo 成功捕获 64 位容器 ID: {containerId}");
                }
            }

            if (string.IsNullOrWhiteSpace(containerId)) return null;

            var process = new Process();
            process.StartInfo.FileName = "docker";

            process.StartInfo.Arguments = $"inspect --format \"{{{{range .Mounts}}}}{{{{if eq .Destination \\\"/app/DaemonData\\\"}}}}{{{{.Source}}}}{{{{end}}}}{{{{end}}}}\" {containerId}";
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;

            process.Start();
            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await Task.Run(process.WaitForExit);

            if (!string.IsNullOrWhiteSpace(error))
            {
                _logger.LogWarning($"[Docker-Inspector] docker inspect 错误: {error.Trim()}");
            }

            string hostPath = output.Trim().Replace("\"", "");
            return string.IsNullOrWhiteSpace(hostPath) ? null : hostPath;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"[Docker-Inspector] 实例 {instanceId} 逆向反查宿主机物理路径时发生致命异常。");
            return null;
        }
    }

    /// <summary>
    /// 应用Docker限速逻辑


    public async Task ApplyDockerNetworkLimitAsync(string gameContainerName, string uploadRate, string downloadRate)
    {
        if (string.IsNullOrWhiteSpace(uploadRate) && string.IsNullOrWhiteSpace(downloadRate)) return;

        try
        {
            // 宿主机不是Linux警告
            bool isProcessWinOrMac = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows) ||
                                     System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX);
            var dockerInfo = await Cli.Wrap("docker")
                .WithArguments(new[] { "info", "--format", "{{.OSType}}" })
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync();

            string osType = dockerInfo.StandardOutput.Trim().ToLower();

            if (isProcessWinOrMac || osType == "windows" || osType == "darwin")
            {
                string actualOs = isProcessWinOrMac
                    ? System.Runtime.InteropServices.RuntimeInformation.OSDescription
                    : osType;

                _logger.LogWarning($"[Network-Limit] 实例容器 {gameContainerName} 处于非原生 Linux 环境，流控可能失效。当前环境: {actualOs}");
            }

            // 处理单位转换
            string ConvertToTcRate(string rateStr)
            {
                var lower = rateStr.ToLower().Trim();
                var match = System.Text.RegularExpressions.Regex.Match(lower, @"\d+(\.\d+)?");
                if (!match.Success) return "100mbit";

                double number = double.Parse(match.Value, System.Globalization.CultureInfo.InvariantCulture);

                if (lower.Contains("mbps") || lower.Contains("mbit"))
                {
                    return $"{Convert.ToInt32(number)}mbit";
                }

                if (lower.Contains("mb") || lower.Contains("m"))
                {
                    return $"{Convert.ToInt32(number * 8)}mbit";
                }

                if (lower.Contains("kbps") || lower.Contains("kbit"))
                {
                    return $"{Convert.ToInt32(number)}kbit";
                }

                return $"{Convert.ToInt32(number)}kbit";
            }

            // 调用工具容器
            async Task<BufferedCommandResult> RunTcSidecarAsync(string tcCommand)
            {
                return await Cli.Wrap("docker")
                    .WithArguments(new[]
                    {
                     "run", "--rm",
                     "--cap-add=NET_ADMIN",                  // 工具容器赋予网络特权
                     $"--net=container:{gameContainerName}",  // 潜入游戏容器的网络空间
                     "docker.mslmc.cn/xiaoyululu/mslx-runtime:network-tool", // 轻量工具容器镜像
                     "sh", "-c", tcCommand
                    })
                    .WithValidation(CommandResultValidation.None)
                    .ExecuteBufferedAsync();
            }

            _logger.LogInformation($"[Network-Limit] 正在为游戏实例容器 {gameContainerName} 挂载安全外壳限速...");

            // 清理可能存在的旧规则
            await RunTcSidecarAsync("tc qdisc del dev eth0 root 2>/dev/null || true");
            await RunTcSidecarAsync("tc qdisc del dev eth0 ingress 2>/dev/null || true");

            // 上传限速 (容器发出的流 - Egress)
            if (!string.IsNullOrWhiteSpace(uploadRate))
            {
                string tcUpload = ConvertToTcRate(uploadRate);
                var result = await RunTcSidecarAsync($"tc qdisc add dev eth0 root tbf rate {tcUpload} burst 32kbit latency 400ms");

                if (result.ExitCode == 0)
                    _logger.LogInformation($"[Network-Limit] 成功限制容器 {gameContainerName} 的上传速率为: {tcUpload}");
                else
                    _logger.LogWarning($"[Network-Limit] 容器 {gameContainerName} 上传限速失败: {result.StandardError}");
            }

            // 下载限速 (流入容器的流 - Ingress)
            if (!string.IsNullOrWhiteSpace(downloadRate))
            {
                string tcDownload = ConvertToTcRate(downloadRate);
                await RunTcSidecarAsync("tc qdisc add dev eth0 handle ffff: ingress");
                var result = await RunTcSidecarAsync($"tc filter add dev eth0 parent ffff: protocol ip prio 50 u32 match ip src 0.0.0.0/0 police rate {tcDownload} burst 32kbit drop flowid :1");

                if (result.ExitCode == 0)
                    _logger.LogInformation($"[Network-Limit] 成功限制容器 {gameContainerName} 的下载速率为: {tcDownload}");
                else
                    _logger.LogWarning($"[Network-Limit] 容器 {gameContainerName} 下载限速失败: {result.StandardError}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"[Network-Limit] 商业化安全限速挂载遭遇严重异常: {ex.Message}");
        }
    }



    public async Task LaunchAsync(uint instanceId, ServerContext context,
        McServerInfo.ServerInfo serverInfo, bool skipEulaCheck, bool isAutoRestart, Action<uint, ServerContext, int> onExit)
    {
        try
        {
            var startingArgs = new ServerStartingEventArgs
            {
                InstanceId = instanceId,
                ServerInfo = serverInfo,
                IsAutoRestart = isAutoRestart,
                Timestamp = DateTime.Now
            };
            _events.PublishServerStarting(startingArgs);
            if (startingArgs.Cancel)
            {
                _console.RecordLog(instanceId, context, $">>> [MSLX] 启动已被插件取消: {startingArgs.CancelReason ?? "无"}");
                _logger.LogInformation($"实例 [{instanceId}] 启动已被插件取消: {startingArgs.CancelReason ?? "无"}");
                _stateStore.Remove(instanceId);
                return;
            }

            if (serverInfo.ExpireTime.HasValue && serverInfo.ExpireTime.Value <= DateTime.Now)
            {
                _console.RecordLog(instanceId, context, $">>> [MSLX] ❌ 启动失败：当前服务端实例已于 {serverInfo.ExpireTime.Value:yyyy-MM-dd HH:mm:ss} 过期。");
                _logger.LogWarning($"实例 [{instanceId}] 启动失败，原因：已过期。");
                _stateStore.Remove(instanceId);
                return;
            }

            _console.RecordLog(instanceId, context, "[MSLX-Daemon] 正在初始化服务...");
            // 检查Eula
            //if (serverInfo.Java != "none" && !serverInfo.IgnoreEula && !skipEulaCheck)
            if (!serverInfo.IgnoreEula && !skipEulaCheck)
            {
                string eulaPath = ServerPropertiesPathUtils.ResolveEulaPath(serverInfo);
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
                    // 发送 EULA 未同意提示
                    _console.RecordLog(instanceId, context,
                        ">>> [MSLX] 检测到 EULA 协议尚未签署，服务器启动已停止，等待用户操作...");
                    _ = _hubContext.Clients.Group(instanceId.ToString()).SendAsync("RequireEULA");
                    _stateStore.Remove(instanceId);
                    return;
                }
            }

            // 自动配置RCON
            if (serverInfo.RconMode == "mc")
            {
                try
                {
                    string propsPath = Utils.ServerPropertiesPathUtils.ResolveFullPath(serverInfo);
                    bool enableRcon = false;
                    bool hasRconPort = false;
                    bool hasRconPassword = false;
                    
                    if (File.Exists(propsPath))
                    {
                        var lines = File.ReadAllLines(propsPath).ToList();
                        bool changed = false;
                        for (int i = 0; i < lines.Count; i++)
                        {
                            if (lines[i].StartsWith("enable-rcon="))
                            {
                                enableRcon = true;
                                if (lines[i] != "enable-rcon=true") { lines[i] = "enable-rcon=true"; changed = true; }
                            }
                            else if (lines[i].StartsWith("rcon.port="))
                            {
                                hasRconPort = true;
                                if (lines[i].Trim() == "rcon.port=") { lines[i] = $"rcon.port={new Random().Next(10000, 60000)}"; changed = true; }
                            }
                            else if (lines[i].StartsWith("rcon.password="))
                            {
                                hasRconPassword = true;
                                if (lines[i].Trim() == "rcon.password=") { lines[i] = $"rcon.password={Guid.NewGuid().ToString("N").Substring(0, 8)}"; changed = true; }
                            }
                        }
                        
                        if (!enableRcon) { lines.Add("enable-rcon=true"); changed = true; }
                        if (!hasRconPort) { lines.Add($"rcon.port={new Random().Next(10000, 60000)}"); changed = true; }
                        if (!hasRconPassword) { lines.Add($"rcon.password={Guid.NewGuid().ToString("N").Substring(0, 8)}"); changed = true; }
                        
                        if (changed) File.WriteAllLines(propsPath, lines);
                    }
                }
                catch { }
            }

            // 检查核心文件是否存在
            if (serverInfo.Core != "none" && !serverInfo.Core.Contains("@libraries"))
            {
                string coreFilePath = Path.Combine(serverInfo.Base, serverInfo.Core);
                if (!File.Exists(coreFilePath))
                {
                    _console.RecordLog(instanceId, context, $">>> [MSLX-MCServer] 核心文件不存在: {coreFilePath}");
                    _stateStore.Remove(instanceId);
                    return;
                }
            }

            // 检查 Java 是否存在 (非Docker模式下进行路径拦截)
            if (serverInfo.Java != "docker-java" && serverInfo.Java != "docker-custom")
            {
                if (!File.Exists(serverInfo.Java) && serverInfo.Java != "java" && serverInfo.Java != "none" &&
                    !serverInfo.Java.StartsWith("MSLX://Java/"))
                {
                    _console.RecordLog(instanceId, context, $">>> [MSLX-MCServer] Java 路径无效: {serverInfo.Java}");
                    _stateStore.Remove(instanceId);
                    return;
                }

                if (serverInfo.Java.StartsWith("MSLX://Java/"))
                {
                    string javaVersion = serverInfo.Java.Replace("MSLX://Java/", "");
                    string javaBaseDir = Path.Combine(IConfigBase.GetAppDataPath(), "Tools", "Java");
                    string javaPath = Path.Combine(javaBaseDir, javaVersion, "bin",
                        PlatFormServices.GetOs() == "Windows" ? "java.exe" : "java");
                    if (!File.Exists(javaPath))
                    {
                        _console.RecordLog(instanceId, context, $">>> [MSLX-MCServer] Java 无效！请尝试重新设置 Java 环境！");
                        _stateStore.Remove(instanceId);
                        return;
                    }
                }
            }

            // 处理外置登录
            string authJvm = "";
            if (!string.IsNullOrEmpty(serverInfo.YggdrasilApiAddr))
            {
                if (!await DownloadAuthlib(serverInfo.Base, instanceId, context))
                {
                    _console.RecordLog(instanceId, context, $">>> [MSLX-MCServer] 外置登录库下载失败！将不启用外置登录......");
                }
                else
                {
                    authJvm = $"-javaagent:authlib-injector.jar={serverInfo.YggdrasilApiAddr}";
                }
            }

            // 给予执行权限
            ExecutePermission.GrantExecutePermission(serverInfo.Base);
            await Task.Delay(100);

            string args = "";
            string exec = "";

            // 是否docker模式
            if (serverInfo.Java == "docker-java" || serverInfo.Java == "docker-custom")
            {
                exec = OperatingSystem.IsWindows() ? "docker.exe" : "docker";
                var sb = new StringBuilder();

                // 基础运行参数
                sb.Append("run --rm -i ");
                sb.Append($"--name mslx-container-{instanceId} ");

                string finalHostBaseDir = serverInfo.Base; // 默认使用宿主机物理路径

                // 检查是否MSLX已经在Docker内，如果是，那么需要进行路径修正
                if (IsRunningInContainer())
                {
                    // 检查是否正确挂载
                    if (!File.Exists("/var/run/docker.sock"))
                    {
                        _logger.LogError($"[MSLX-Daemonr] ❌ 容器化运行严重错误：未检测到 Docker 通信管道（/var/run/docker.sock）！");
                        _console.RecordLog(instanceId, context, $"[MSLX-Daemon] ❌ 错误：MSLX-Daemon 处于 Docker 容器中运行，但未挂载宿主机的 Sock 管道！");
                        _console.RecordLog(instanceId, context, $"[MSLX-Daemon] 💡 解决办法：请检查部署命令/Compose配置文件，确保挂载了以下路径：/var/run/docker.sock:/var/run/docker.sock");
                        _console.RecordLog(instanceId, context, $"[MSLX-Daemon] Docker部署MSLX文档: https://mslx.mslmc.cn/docs/install/docker/ ");
                        _console.RecordLog(instanceId, context, $"[MSLX-Daemon] MSLX运行Docker服务端文档: https://mslx.mslmc.cn/docs/server/docker/");
                        _console.RecordLog(instanceId, context, $"[MSLX-Daemon] (重点查看《MSLX已运行在Docker下，如何再部署Docker服务端实例？》)\n");
                        _stateStore.Remove(instanceId); 
                        _console.RecordLog(instanceId, context, $"[MSLX] 服务端启动已取消！");
                        return;
                    }
                    _console.RecordLog(instanceId, context, "[MSLX-Daemon] 检测到当前 MSLX-Daemon 处于容器内，正在查询物理主机挂载路径...");
                    string? hostDataRoot = await GetHostPhysicalDataPathAsync(instanceId, context);

                    if (!string.IsNullOrWhiteSpace(hostDataRoot))
                    {
                        finalHostBaseDir = serverInfo.Base.Replace("/app/DaemonData", hostDataRoot);
                        _logger.LogInformation($"[MSLX-Daemon] 路径转换完成: {serverInfo.Base} -> {finalHostBaseDir}");
                    }
                    else
                    {
                        _console.RecordLog(instanceId, context, "[MSLX-Daemon] 查询物理路径失败，将尝试使用原始路径。");
                    }
                }

                // 工作目录与基础挂载
                string workDir = string.IsNullOrWhiteSpace(serverInfo.DockerWorkingDir) ? "/mslx-data" : serverInfo.DockerWorkingDir;
                sb.Append($"-v \"{finalHostBaseDir}:{workDir}\" ");
                sb.Append($"-w \"{workDir}\" ");

                // 挂载额外目录卷
                if (!string.IsNullOrWhiteSpace(serverInfo.DockerVolumes))
                {
                    var volumes = serverInfo.DockerVolumes.Split(',', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var vol in volumes)
                    {
                        sb.Append($"-v \"{vol.Trim()}\" ");
                    }
                }

                // 网络模式与别名、端口映射
                string netMode = string.IsNullOrWhiteSpace(serverInfo.DockerNetworkMode) ? "bridge" : serverInfo.DockerNetworkMode.ToLower();
                if (serverInfo.DockerPorts?.Trim() == "0")
                {
                    netMode = "host";
                }
                sb.Append($"--net={netMode} ");

                if (netMode == "bridge" && !string.IsNullOrWhiteSpace(serverInfo.DockerPorts) && serverInfo.DockerPorts.Trim() != "0")
                {
                    var ports = serverInfo.DockerPorts.Split(',', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var port in ports)
                    {
                        sb.Append($"-p {port.Trim()} ");
                    }
                }

                if (netMode != "host" && netMode != "none" && netMode != "bridge" && !string.IsNullOrWhiteSpace(serverInfo.DockerNetworkAlias))
                {
                    sb.Append($"--network-alias=\"{serverInfo.DockerNetworkAlias.Trim()}\" ");
                }

                // 环境变量加载
                if (!string.IsNullOrWhiteSpace(serverInfo.DockerEnvVars))
                {
                    var envs = serverInfo.DockerEnvVars.Split(',', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var env in envs)
                    {
                        sb.Append($"-e {env.Trim()} ");
                    }
                }

                // 额外hosts
                if (!string.IsNullOrWhiteSpace(serverInfo.DockerExtraHosts))
                {
                    var hosts = serverInfo.DockerExtraHosts.Split(',', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var host in hosts)
                    {
                        sb.Append($"--add-host=\"{host.Trim()}\" ");
                    }
                }

                // Cgroups 隔离与硬核网络IO限制
                if (!string.IsNullOrWhiteSpace(serverInfo.DockerCpuCores))
                {
                    sb.Append($"--cpuset-cpus=\"{serverInfo.DockerCpuCores.Trim()}\" ");
                }
                if (serverInfo.DockerCpuPercentage is > 0)
                {
                    double coresCount = serverInfo.DockerCpuPercentage.Value / 100.0;
                    if (coresCount > Environment.ProcessorCount)
                    {
                        coresCount = Environment.ProcessorCount;
                    }
                    sb.Append(string.Format(System.Globalization.CultureInfo.InvariantCulture, "--cpus=\"{0:F2}\" ", coresCount));
                }
                if (serverInfo.DockerMaxMemoryMb is > 0)
                {
                    sb.Append($"-m {serverInfo.DockerMaxMemoryMb.Value}m ");
                }
                // 0 代表禁用 Swap，条件是 >= 0，且必须有物理内存限制
                if (serverInfo.DockerMaxSwapMb.HasValue && serverInfo.DockerMaxSwapMb.Value >= 0 && serverInfo.DockerMaxMemoryMb is > 0)
                {
                    sb.Append($"--memory-swap {serverInfo.DockerMaxSwapMb.Value}m ");
                }
                if (PlatFormServices.GetOs() == "Linux" && !string.IsNullOrWhiteSpace(serverInfo.DockerMaxStorage))
                {
                    sb.Append($"--storage-opt size={serverInfo.DockerMaxStorage.Trim()} ");
                }

                // 额外原生参数透传
                if (!string.IsNullOrWhiteSpace(serverInfo.DockerExtraArgs))
                {
                    sb.Append($"{serverInfo.DockerExtraArgs.Trim()} ");
                }

                // 获取完整镜像
                string rawImage = string.IsNullOrWhiteSpace(serverInfo.DockerImage)
                    ? $"{DockerImageResolver.PseudoPrefix}21"
                    : serverInfo.DockerImage;
                string finalImage = DockerImageResolver.Resolve(rawImage);

                if (DockerImageResolver.IsPseudo(rawImage))
                {
                    _logger.LogInformation($"[Docker-Parser] 实例 {instanceId} 命中内置运行时伪协议，解析镜像: {rawImage} -> {finalImage}");
                }

                sb.Append($"{finalImage} ");

                // 组装一些内置参数
                if (serverInfo.Java == "docker-java")
                {
                    string javaArgs = "";
                    if (!string.IsNullOrWhiteSpace(authJvm)) javaArgs += $"{authJvm.Trim()} "; // 外置登录
                    if (serverInfo.MinM.HasValue) javaArgs += $"-Xms{serverInfo.MinM.Value}M "; // JVM内存
                    if (serverInfo.MaxM.HasValue) javaArgs += $"-Xmx{serverInfo.MaxM.Value}M ";

                    if (!string.IsNullOrWhiteSpace(serverInfo.Args)) javaArgs += $"{serverInfo.Args.Trim()} "; // 额外参数
                    if (serverInfo.ForceJvmUTF8) javaArgs += "-Dfile.encoding=UTF-8 "; // 强制UTF8

                    // 服务端核心
                    if (serverInfo.Core.Contains("@libraries"))
                    {
                        javaArgs += $"{serverInfo.Core.Trim()} nogui";
                    }
                    else
                    {
                        javaArgs += $"-jar {serverInfo.Core.Trim()} nogui";
                    }

                    sb.Append($"java {javaArgs.Trim()}");
                }
                else // docker-custom 完全自定义模式
                {
                    sb.Append(serverInfo.Args.Trim());
                }

                args = sb.ToString();
            }
            else
            {
                string terminalColorAndJline = serverInfo.EnablePty
                    ? " -Dterminal.ansi=true"
                    : (serverInfo.AllowOriginASCIIColors ? " -Dterminal.jline=false -Dterminal.ansi=true" : "");

                string jvmUtf8 = (serverInfo.ForceJvmUTF8 || serverInfo.EnablePty)
                    ? (OperatingSystem.IsWindows()
                        ? " -Dfile.encoding=UTF-8 -Dsun.stdout.encoding=UTF-8 -Dsun.stderr.encoding=UTF-8 -Dsun.stdin.encoding=UTF-8 -Dstdout.encoding=UTF-8 -Dstderr.encoding=UTF-8 -Dstdin.encoding=UTF-8"
                        : " -Dfile.encoding=UTF-8")
                    : "";

                // 主机直接启动
                args =
                    $"{authJvm} -Xms{serverInfo.MinM}M -Xmx{serverInfo.MaxM}M {serverInfo.Args}{jvmUtf8}{terminalColorAndJline} -jar {serverInfo.Core} nogui";
                exec = serverInfo.Java;

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
                    string javaBaseDir = Path.Combine(IConfigBase.GetAppDataPath(), "Tools", "Java");
                    exec = Path.Combine(javaBaseDir, javaVersion, "bin",
                        PlatFormServices.GetOs() == "Windows" ? "java.exe" : "java");
                }

                // 处理NeoForge类型参数
                if (serverInfo.Core.Contains("@libraries"))
                {
                    args =
                        $"{authJvm} -Xms{serverInfo.MinM}M -Xmx{serverInfo.MaxM}M {serverInfo.Args}{jvmUtf8}{terminalColorAndJline} {serverInfo.Core} nogui";
                }
            }

            // 处理编码
            Encoding inputEncoding = EncodingUtils.GetEncodingOrDefault(serverInfo.InputEncoding,
                name => _logger.LogWarning($"无法识别编码: {name}，已回退到 UTF-8 (No BOM)"));
            Encoding outputEncoding = EncodingUtils.GetEncodingOrDefault(serverInfo.OutputEncoding,
                name => _logger.LogWarning($"无法识别编码: {name}，已回退到 UTF-8 (No BOM)"));
            _logger.LogInformation(
                $"实例 {instanceId} 编码设置 - 输入: {inputEncoding.EncodingName}, 输出: {outputEncoding.EncodingName}，JVM强制UTF8：{serverInfo.ForceJvmUTF8}");

            // 配置启动参数
            var startInfo = new ProcessStartInfo
            {
                WorkingDirectory = serverInfo.Base,
                FileName = exec,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                // 编码配置
                StandardOutputEncoding = outputEncoding,
                StandardErrorEncoding = outputEncoding,
                StandardInputEncoding = inputEncoding
            };

            // 注入环境变量让终端输出原彩ASCII
            if (serverInfo.AllowOriginASCIIColors)
            {
                if (!startInfo.EnvironmentVariables.ContainsKey("TERM"))
                {
                    startInfo.EnvironmentVariables.Add("TERM", "xterm-256color");
                }

                if (!startInfo.EnvironmentVariables.ContainsKey("COLORTERM"))
                {
                    startInfo.EnvironmentVariables.Add("COLORTERM", "truecolor");
                }

                if (!startInfo.EnvironmentVariables.ContainsKey("FORCE_COLOR"))
                {
                    startInfo.EnvironmentVariables.Add("FORCE_COLOR", "1");
                }
            }

            // 自定义模式且为python软件时，注入UTF-8
            if (serverInfo.Java == "none" && (serverInfo.Args?.ToLower().Contains("python") ?? false))
            {
                if (!startInfo.EnvironmentVariables.ContainsKey("PYTHONIOENCODING"))
                {
                    startInfo.EnvironmentVariables.Add("PYTHONIOENCODING", "utf-8");
                }

                if (!startInfo.EnvironmentVariables.ContainsKey("PYTHONUTF8"))
                {
                    startInfo.EnvironmentVariables.Add("PYTHONUTF8", "1");
                }
            }

            var process = new Process { StartInfo = startInfo };

            process.EnableRaisingEvents = true;

            // 绑定事件
            process.Exited += (sender, e) =>
            {
                if (sender is Process p)
                {
                    lock (context.StateLock)
                    {
                        context.IsProcessExited = true;
                        context.FinalExitCode = p.ExitCode; // 暂存退出代码
                    }

                    // 真退出吗哥？
                    CheckAndHandleTrueExit(instanceId, context, onExit);
                }
            };

            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data == null) // EOF了
                {
                    lock (context.StateLock)
                    {
                        context.IsStdoutClosed = true;
                    }

                    CheckAndHandleTrueExit(instanceId, context, onExit);
                }
                else
                {
                    _console.RecordLog(instanceId, context, e.Data);
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data == null) // EOF了
                {
                    lock (context.StateLock)
                    {
                        context.IsStderrClosed = true;
                    }

                    CheckAndHandleTrueExit(instanceId, context, onExit);
                }
                else
                {
                    _console.RecordLog(instanceId, context, e.Data);
                }
            };

            _console.RecordLog(instanceId, context, "[MSLX-Daemon] 正在启动服务端实例...");

            // 处理玩家监听
            context.MonitorPlayers = serverInfo.MonitorPlayers;
            context.InputEncoding = inputEncoding;
            context.OutputEncoding = outputEncoding;
            
            // Docker模式保存性能监视基准参数
            context.IsDocker = serverInfo.Java == "docker-java" || serverInfo.Java == "docker-custom";

            // 预计算CPU基准
            if (context.IsDocker)
            {
                if (serverInfo.DockerCpuPercentage is > 0)
                {
                    context.CpuBaseLimitPercentage = serverInfo.DockerCpuPercentage.Value;
                }
                else if (!string.IsNullOrWhiteSpace(serverInfo.DockerCpuCores))
                {
                    int coreCount = serverInfo.DockerCpuCores.Split(',', StringSplitOptions.RemoveEmptyEntries).Length;
                    if (coreCount > 0) context.CpuBaseLimitPercentage = coreCount * 100.0;
                }

                // 未配置，默认回退到宿主机总核心百分比
                if (context.CpuBaseLimitPercentage <= 0)
                {
                    context.CpuBaseLimitPercentage = Environment.ProcessorCount * 100.0;
                }
            }

            // 启动进程
            bool started = false;

            if (serverInfo.EnablePty)
            {
                // PTY 仿真终端启动流程
                try
                {
                    var envDict = new Dictionary<string, string>();
                    foreach (System.Collections.DictionaryEntry de in startInfo.EnvironmentVariables)
                    {
                        if (de.Key != null && de.Value != null)
                        {
                            envDict[de.Key.ToString()!] = de.Value.ToString()!;
                        }
                    }
                    if (!envDict.ContainsKey("TERM")) envDict["TERM"] = "xterm-256color";
                    if (!envDict.ContainsKey("COLORTERM")) envDict["COLORTERM"] = "truecolor";
                    if (!envDict.ContainsKey("FORCE_COLOR")) envDict["FORCE_COLOR"] = "1";
                    var hostLang = Environment.GetEnvironmentVariable("LANG");
                    if (!envDict.ContainsKey("LANG")) envDict["LANG"] = !string.IsNullOrWhiteSpace(hostLang) ? hostLang : "zh_CN.UTF-8";
                    if (!envDict.ContainsKey("LC_ALL")) envDict["LC_ALL"] = envDict["LANG"];

                    int initCols = 120;
                    int initRows = 30;
                    if (_stateStore.TryGetPreferredTerminalSize(instanceId, out var prefSize) && prefSize.cols > 0 && prefSize.rows > 0)
                    {
                        initCols = prefSize.cols;
                        initRows = prefSize.rows;
                    }

                    string ptyApp = exec;
                    string[] ptyArgs = CommandLineUtils.SplitCommandLineArgs(args);
                    bool verbatim = false;

                    if (OperatingSystem.IsWindows() && !context.IsDocker)
                    {
                        if (ptyApp.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase) ||
                            ptyApp.EndsWith("\\cmd.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            if (args != null && !args.Contains("chcp 65001"))
                            {
                                string cleanArgs = args.TrimStart();
                                if (cleanArgs.StartsWith("/c", StringComparison.OrdinalIgnoreCase))
                                {
                                    cleanArgs = cleanArgs.Substring(2).TrimStart();
                                }
                                ptyArgs = new[] { "/c", $"chcp 65001 >nul && {cleanArgs}" };
                                verbatim = true;
                            }
                        }
                        else
                        {
                            ptyApp = "cmd.exe";
                            ptyArgs = new[] { "/c", $"chcp 65001 >nul && \"{exec}\" {args}" };
                            verbatim = true;
                        }
                    }

                    var ptyOptions = new PtyOptions
                    {
                        Name = $"MSLX-{instanceId}",
                        Cols = initCols,
                        Rows = initRows,
                        Cwd = serverInfo.Base,
                        App = ptyApp,
                        CommandLine = ptyArgs,
                        VerbatimCommandLine = verbatim,
                        Environment = envDict
                    };

                    IPtyConnection ptyConnection = await PtyProvider.SpawnAsync(ptyOptions, CancellationToken.None);
                    context.PtyConnection = ptyConnection;
                    context.IsPtyMode = true;

                    try
                    {
                        context.Process = Process.GetProcessById(ptyConnection.Pid);
                        ProcessTracker.Track(context.Process, false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"[PTY] 无法获取或跟踪进程 PID {ptyConnection.Pid}: {ex.Message}");
                    }

                    context.IsInitializing = false;

                    ptyConnection.ProcessExited += (sender, e) =>
                    {
                        lock (context.StateLock)
                        {
                            context.IsProcessExited = true;
                            context.FinalExitCode = ptyConnection.ExitCode;
                        }
                        try { context.PtyReadCts?.CancelAfter(300); } catch { }
                        CheckAndHandleTrueExit(instanceId, context, onExit);
                    };

                    var ptyCts = new CancellationTokenSource();
                    context.PtyReadCts = ptyCts;

                    _ = Task.Run(async () =>
                    {
                        byte[] buffer = new byte[4096];
                        char[] chars = new char[4096];
                        var decoder = Encoding.UTF8.GetDecoder();
                        var lineSb = new StringBuilder();
                        try
                        {
                            while (!ptyCts.Token.IsCancellationRequested)
                            {
                                int read = await ptyConnection.ReaderStream.ReadAsync(buffer, 0, buffer.Length, ptyCts.Token);
                                if (read <= 0) break;

                                int charCount = decoder.GetChars(buffer, 0, read, chars, 0, false);
                                string rawChunk = new string(chars, 0, charCount);
                                _console.AppendPtyHistory(context, rawChunk);
                                await _hubContext.Clients.Group("pty_" + instanceId).SendAsync("ReceivePtyData", rawChunk);

                                for (int i = 0; i < charCount; i++)
                                {
                                    char c = chars[i];
                                    if (c == '\n')
                                    {
                                        string line = lineSb.ToString().TrimEnd('\r');
                                        lineSb.Clear();
                                        _console.RecordLog(instanceId, context, line);
                                    }
                                    else
                                    {
                                        lineSb.Append(c);
                                    }
                                }
                            }
                        }
                        catch (OperationCanceledException) { }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"[PTY] 读取输出异常: {ex.Message}");
                        }
                        finally
                        {
                            if (lineSb.Length > 0)
                            {
                                _console.RecordLog(instanceId, context, lineSb.ToString().TrimEnd('\r'));
                            }
                            lock (context.StateLock)
                            {
                                context.IsStdoutClosed = true;
                                context.IsStderrClosed = true;
                            }
                            CheckAndHandleTrueExit(instanceId, context, onExit);
                        }
                    });

                    _logger.LogInformation($"服务器 [{instanceId}] 以 PTY 模式启动成功，PID: {ptyConnection.Pid}");
                    _console.RecordLog(instanceId, context, $"[MSLX] 服务器进程已通过 PTY 启动，PID: {ptyConnection.Pid}");
                    started = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"[PTY] 启动 PTY 失败，将回退到标准流模式: {ex.Message}");
                    _console.RecordLog(instanceId, context, $">>> [MSLX] 启动 PTY 失败 ({ex.Message})，正在回退到标准流模式...");
                    context.IsPtyMode = false;
                    context.PtyConnection = null;
                }
            }

            if (!started)
            {
                if (process.Start())
                {
                    ProcessTracker.Track(process, false);
                    context.Process = process;
                    context.IsInitializing = false; // 初始化完成

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    _logger.LogInformation($"服务器 [{instanceId}] 启动成功，PID: {process.Id}");
                    _console.RecordLog(instanceId, context, $"[MSLX] 服务器进程已启动，PID: {process.Id}");
                    started = true;
                }
            }

            if (started)
            {
                int pid = context.IsPtyMode && context.PtyConnection != null ? context.PtyConnection.Pid : (context.Process?.Id ?? 0);
                _events.PublishServerStarted(new ServerStartedEventArgs
                {
                    InstanceId = instanceId,
                    ServerInfo = serverInfo,
                    ProcessId = pid,
                    Timestamp = DateTime.Now
                });

                // 联动启动隧道
                if (!string.IsNullOrWhiteSpace(serverInfo.BindFrpId))
                {
                    var frpIds = serverInfo.BindFrpId.Split(',', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var idStr in frpIds)
                    {
                        if (int.TryParse(idStr.Trim(), out int frpId))
                        {
                            _console.RecordLog(instanceId, context, $"[MSLX-Daemon] 检测到联动绑定，正在后台启动隧道 [{frpId}]...");
                            var (frpSuccess, frpMsg) = _frpService.StartFrp(frpId);
                            if (!frpSuccess)
                            {
                                _console.RecordLog(instanceId, context, $">>> [MSLX-Daemon] ⚠️ 联动隧道 [{frpId}] 启动失败: {frpMsg}");
                            }
                        }
                    }
                }

                // 启动docker流控
                if ((serverInfo.Java == "docker-java" || serverInfo.Java == "docker-custom") &&
                (!string.IsNullOrWhiteSpace(serverInfo.DockerUploadRate) || !string.IsNullOrWhiteSpace(serverInfo.DockerDownloadRate)))
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(2022 + 1102);

                            await ApplyDockerNetworkLimitAsync(
                                $"mslx-container-{instanceId}",
                                serverInfo.DockerUploadRate,
                                serverInfo.DockerDownloadRate
                            );
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"[MSLX-Daemon] 实例 [{instanceId}] 异步挂载网络流控失败: {ex.Message}");
                        }
                    });
                }
            }
            else
            {
                _console.RecordLog(instanceId, context, ">>> [MSLX-MCServer] 进程启动失败！");
                _stateStore.Remove(instanceId);
            }
        }
        catch (Exception ex)
        {
            _console.RecordLog(instanceId, context, $">>> [MSLX-MCServer] 启动流程发生未捕获异常: {ex.Message}");
            _logger.LogError(ex, $"MC 服务器 [{instanceId}] 启动异常");
            _stateStore.Remove(instanceId);
        }
    }


    private void CheckAndHandleTrueExit(uint instanceId, ServerContext context, Action<uint, ServerContext, int> onExit)
    {
        lock (context.StateLock)
        {
            // 来过了就别来了哇
            if (context.HasTriggeredExit) return;

            // 模式区分：
            // PTY 模式下，当 pty 进程退出时，进程已经确定退出；由于 Windows ConPTY 的 ReaderStream 在子进程退出时不会主动关闭 EOF，
            // 只要 context.IsProcessExited 即可触发退出，并取消读取流和释放 pty。
            // 普通标准流模式下，需确保 stdout 和 stderr 均读到 EOF 避免丢日志。
            bool shouldExit = context.IsPtyMode
                ? context.IsProcessExited
                : (context.IsProcessExited && context.IsStdoutClosed && context.IsStderrClosed);

            if (shouldExit)
            {
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

                onExit?.Invoke(instanceId, context, context.FinalExitCode);
            }
        }
    }

    /// <summary>
    /// 获取服务器已运行的时间


    private async Task<bool> DownloadAuthlib(string basePath, uint instanceId, ServerContext context)
    {
        try
        {
            HttpService.HttpResponse response =
                await GeneralApi.GetAsync("https://authlib-injector.mirrors.mslmc.cn/artifact/latest.json");

            if (response.IsSuccessStatusCode)
            {
                // 检查响应内容是否为空
                if (string.IsNullOrWhiteSpace(response.Content))
                {
                    _logger.LogError("下载Authlib-Injector失败: 响应内容为空。");
                    return File.Exists(Path.Combine(basePath, "authlib-injector.jar"));
                }

                JObject? authlibJobj = JObject.Parse(response.Content);

                // 检查 JSON 解析结果和必需字段
                if (authlibJobj == null)
                {
                    _logger.LogError("下载Authlib-Injector失败: JSON 解析失败。");
                    return File.Exists(Path.Combine(basePath, "authlib-injector.jar"));
                }

                // 获取 checksums.sha256
                var sha256Token = authlibJobj["checksums"]?["sha256"];
                var sha256 = sha256Token?.ToString();

                if (string.IsNullOrEmpty(sha256))
                {
                    _logger.LogError("下载Authlib-Injector失败: 无法获取 SHA256 校验值。");
                    return File.Exists(Path.Combine(basePath, "authlib-injector.jar"));
                }

                // 获取 download_url
                var downloadUrlToken = authlibJobj["download_url"];
                var downloadUrl = downloadUrlToken?.ToString();

                if (string.IsNullOrEmpty(downloadUrl))
                {
                    _logger.LogError("下载Authlib-Injector失败: 无法获取下载地址。");
                    return File.Exists(Path.Combine(basePath, "authlib-injector.jar"));
                }

                var authlibPath = Path.Combine(basePath, "authlib-injector.jar");

                // 检查是否需要下载
                if (!File.Exists(authlibPath) || !await FileUtils.ValidateFileSha256Async(authlibPath, sha256))
                {
                    // 下载
                    _console.RecordLog(instanceId, context, $"[MSLX] 正在处理下载外置登录库依赖···");
                    var downloader = new ParallelDownloader(parallelCount: 1);
                    var mirroredUrl = downloadUrl.Replace("authlib-injector.yushi.moe",
                        "authlib-injector.mirrors.mslmc.cn");

                    var (success, errorMsg) = await downloader.DownloadFileAsync(
                        mirroredUrl,
                        authlibPath,
                        // 进度回调
                        async (progress, speed) =>
                        {
                            _console.RecordLog(instanceId, context,
                                $"正在下载 Authlib-Injector... 进度: {progress:0.00}% | 下载速度: {speed}");
                        }
                    );

                    if (!success)
                    {
                        _logger.LogError("下载Authlib-Injector失败: {ErrorMsg}", errorMsg);
                        if (!File.Exists(authlibPath))
                        {
                            return false;
                        }
                    }
                }
            }
            else
            {
                var authlibPath = Path.Combine(basePath, "authlib-injector.jar");
                if (!File.Exists(authlibPath))
                {
                    _logger.LogError("下载Authlib-Injector失败: 无法获取元数据 (HTTP {StatusCode})。",
                        response.StatusCode);
                    return false;
                }

                _logger.LogWarning("获取元数据失败，将使用旧版本Authlib-Injector。");
            }
        }
        catch (Newtonsoft.Json.JsonException jsonEx)
        {
            _logger.LogError(jsonEx, "下载Authlib-Injector失败: JSON 解析错误");
            if (!File.Exists(Path.Combine(basePath, "authlib-injector.jar")))
            {
                return false;
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

}
