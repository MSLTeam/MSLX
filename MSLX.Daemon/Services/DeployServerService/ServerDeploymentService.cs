using CliWrap;
using CliWrap.Buffered;
using CliWrap.EventStream;
using Downloader;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using MSLX.Daemon.Utils.McdrConfig;
using MSLX.SDK.Models;
using MSLX.SDK.Models.Instance;
using Newtonsoft.Json.Linq;
using System;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace MSLX.Daemon.Services.DeployServerService;

/// <summary>
/// 核心部署服务：负责下载、解压、文件移动、Forge安装等具体子操作
/// </summary>
public class ServerDeploymentService
{
    private readonly ILogger<ServerDeploymentService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    // 定义进度回调委托
    public delegate Task ReportProgress(string message, double? progress, bool isError = false, Exception? ex = null);

    public ServerDeploymentService(ILogger<ServerDeploymentService> logger, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// 远程下载压缩包
    /// </summary>
    public async Task<string> DownloadPackageAsync(string serverId, string packageFileUrl, string packageFileSha256,
        ReportProgress report)
    {
        if (string.IsNullOrEmpty(packageFileUrl)) return string.Empty;
        try
        {
            string packageFileKey = Guid.NewGuid().ToString("N");
            string savePath = Path.Combine(IConfigBase.GetAppDataPath(), "Temp", "Uploads", packageFileKey + ".tmp");
            bool success =
                await DownloadAndValidateAsync(packageFileUrl, savePath, $"服务端压缩包文件", packageFileSha256, report);
            if (!success) throw new Exception("服务端压缩文件下载失败！");
            return packageFileKey;
        }
        catch (Exception ex)
        {
            await report($"{ex.Message}", -1, true, ex);
            throw;
        }
    }

    /// <summary>
    /// 处理整合包解压与目录调整
    /// </summary>
    public async Task DeployPackageAsync(string serverId, string? packageFileKey, string? packageLocalPath,
        string targetDir, ReportProgress report)
    {
        if (string.IsNullOrEmpty(packageFileKey) && string.IsNullOrEmpty(packageLocalPath)) return;

        string sourceFilePath = !string.IsNullOrEmpty(packageLocalPath)
            ? packageLocalPath
            : Path.Combine(IConfigBase.GetAppDataPath(), "Temp", "Uploads", packageFileKey + ".tmp");

        if (!File.Exists(sourceFilePath))
        {
            await report("压缩包文件不存在或已过期！", -1, true);
            return;
        }

        try
        {
            _logger.LogInformation("开始解压整合包: {Path}", sourceFilePath);
            await report("正在解压服务端整合包...", 10);

            // 解压
            ZipFile.ExtractToDirectory(sourceFilePath, targetDir, System.Text.Encoding.GetEncoding("GBK"), true);

            // 去除套娃逻辑
            var rootDirs = Directory.GetDirectories(targetDir);
            var rootFiles = Directory.GetFiles(targetDir);

            if (rootFiles.Length == 0 && rootDirs.Length == 1)
            {
                string nestedDir = rootDirs[0];
                await report("检测到多余文件夹，正在调整目录结构...", 15);

                // 移动文件
                foreach (var file in Directory.GetFiles(nestedDir))
                {
                    string destFile = Path.Combine(targetDir, Path.GetFileName(file));
                    File.Move(file, destFile, true);
                }

                // 移动文件夹
                foreach (var dir in Directory.GetDirectories(nestedDir))
                {
                    string destDir = Path.Combine(targetDir, Path.GetFileName(dir));
                    if (Directory.Exists(destDir)) Directory.Delete(destDir, true);
                    Directory.Move(dir, destDir);
                }

                Directory.Delete(nestedDir);
            }

            await report("压缩包解压部署完成。", 20);
        }
        catch (Exception ex)
        {
            await report($"解压整合包失败: {ex.Message}", -1, true, ex);
            throw;
        }
        finally
        {
            if (string.IsNullOrEmpty(packageLocalPath))
            {
                try
                {
                    File.Delete(sourceFilePath);
                }
                catch
                {
                }
            }
        }
    }

    /// <summary>
    /// 基岩版服务端权限处理
    /// </summary>
    public async Task ChmodBedrockServerAsync(string serverId, string targetDir, ReportProgress report)
    {
        try
        {
            if (PlatFormServices.GetOs() != "Linux") return;
            string binPath = Path.Combine(targetDir, "bedrock_server");

            if (File.Exists(binPath))
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = $"+x \"{binPath}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                using var process = System.Diagnostics.Process.Start(psi);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                }
            }
        }
        catch
        {
        }
    }

    /// <summary>
    /// 处理 Java 环境
    /// </summary>
    public async Task EnsureJavaAsync(string serverId, string javaConfig, ReportProgress report)
    {
        if (string.IsNullOrEmpty(javaConfig) || !javaConfig.Contains("MSLX://Java/")) return;

        string javaVersion = javaConfig.Replace("MSLX://Java/", "");
        string javaBaseDir = Path.Combine(IConfigBase.GetAppDataPath(), "Tools", "Java");
        string finalJavaDir = Path.Combine(javaBaseDir, javaVersion);
        string javaExec = PlatFormServices.GetOs() == "Windows" ? "java.exe" : "java";
        string javaPath = Path.Combine(finalJavaDir, "bin", javaExec);

        if (File.Exists(javaPath))
        {
            await report($"Java {javaVersion} 已存在，无需下载。", 0);
            return;
        }

        // 需要下载
        await report($"正在获取 Java {javaVersion} 下载信息...", 0);

        // 调用API获取下载地址
        var response = await MSLApi.GetAsync($"/download/jdk/{javaVersion}",
            new Dictionary<string, string>
            {
                { "arch", PlatFormServices.GetOsArch().Replace("amd64", "x64") },
                { "os", PlatFormServices.GetOs().ToLower().Replace("os", "") }
            });

        if (!response.IsSuccessStatusCode)
        {
            await report($"获取 Java 信息失败: {response.ResponseException}", -1, true);
            throw new Exception("Java download info fetch failed");
        }

        JObject downloadInfo = JObject.Parse(response.Content);
        string? url = downloadInfo["data"]?["url"]?.ToString();
        string? sha256 = downloadInfo["data"]?["sha256"]?.ToString();
        string fileName = PlatFormServices.GetOs() == "Windows" ? $"{javaVersion}.zip" : $"{javaVersion}.tar.gz";
        string savePath = Path.Combine(javaBaseDir, fileName);

        Directory.CreateDirectory(javaBaseDir);

        // 下载
        bool success = await DownloadAndValidateAsync(url, savePath, $"Java {javaVersion}", sha256, report);
        if (!success) throw new Exception("Java download failed");

        // 解压
        try
        {
            await report($"正在配置 Java {javaVersion} 环境...", 99.99);
            await ExtractJavaSmartAsync(savePath, javaVersion, javaBaseDir);
            await report($"Java {javaVersion} 部署成功！", 99.9);
        }
        catch (Exception ex)
        {
            await report($"Java 解压失败: {ex.Message}", -1, true, ex);
            throw;
        }
    }

    /// <summary>
    /// 处理 Docker 运行镜像的检测与拉取
    /// </summary>
    public async Task PullImageIfNeededAsync(string serverId, string realImageName, ReportProgress report)
    {
        if (string.IsNullOrWhiteSpace(realImageName)) return;

        await report($"正在检测本地是否存在镜像 [{realImageName.Replace("docker.mslmc.cn/xiaoyululu/", "")}]...", 0);

        bool imageExists = false;
        try
        {
            var checkResult = await Cli.Wrap("docker")
                .WithArguments(new[] { "inspect", "--type=image", realImageName })
                .WithValidation(CliWrap.CommandResultValidation.None)
                .ExecuteAsync();

            imageExists = checkResult.ExitCode == 0;
        }
        catch (Exception)
        {
            await report("未检测到本地 Docker 守护进程，请确保宿主机已正确安装并拉起 Docker 服务！", -1, true);
            throw new Exception("Docker daemon not running");
        }

        if (imageExists)
        {
            await report($"Docker 镜像 [{realImageName.Replace("docker.mslmc.cn/xiaoyululu/","")}] 本地已存在，跳过拉取流。", 0);
            return;
        }

        await report($"本地未发现镜像，开始从 Registry 仓库拉取 [{realImageName.Replace("docker.mslmc.cn/xiaoyululu/", "")}]...", 5);

        try
        {
            var cmd = CliWrap.Cli.Wrap("docker")
                .WithArguments(new[] { "pull", realImageName })
                .WithValidation(CliWrap.CommandResultValidation.None);

            await foreach (var cmdEvent in cmd.ListenAsync())
            {
                if (cmdEvent is CliWrap.EventStream.StandardOutputCommandEvent outEvent)
                {
                    var output = outEvent.Text?.Trim();
                    if (string.IsNullOrWhiteSpace(output)) continue;

                    if (output.Contains("Pulling from"))
                    {
                        await report($"正在初始化镜像层级下载链...", 15);
                    }
                    else if (output.Contains("Extracting"))
                    {
                        await report($"正在解压并校验镜像层... {FormatDockerLine(output)}", 70);
                    }
                    else if (output.Contains("Download complete"))
                    {
                        await report($"镜像分层数据下载完成...", 85);
                    }
                    else if (output.Contains("Status: Downloaded newer image") || output.Contains("Image is up to date"))
                    {
                        await report($"全量镜像下载成功！", 95);
                    }
                    else
                    {
                        string clipLog = output.Length > 35 ? output.Substring(0, 35) + "..." : output;
                        await report($"[Docker] {clipLog}", 40);
                    }
                }
                else if (cmdEvent is CliWrap.EventStream.StandardErrorCommandEvent errEvent)
                {
                    if (!string.IsNullOrWhiteSpace(errEvent.Text))
                    {
                        _logger.LogWarning("[Docker-Pull-Error] {Err}", errEvent.Text);
                    }
                }
            }

            await report($"Docker 镜像 [{realImageName.Replace("docker.mslmc.cn/xiaoyululu/", "")}] 部署成功！", 99);
        }
        catch (Exception ex)
        {
            await report($"Docker 镜像拉取失败：{ex.Message}。请检查代理网络或 Registry 仓库配置。", -1, true, ex);
            throw;
        }
    }

    private static string FormatDockerLine(string line)
    {
        try
        {
            return line.Length > 20 ? line.Substring(0, 20) : line;
        }
        catch { return "处理中..."; }
    }

    /// <summary>
    /// 部署核心文件 (支持用户上传 或 远程下载)
    /// </summary>
    public async Task DeployCoreAsync(string serverId, string baseDir, string coreName, string? userUploadKey,
        string? downloadUrl, string? sha256, ReportProgress report)
    {
        string destPath = Path.Combine(baseDir, coreName);
        Directory.CreateDirectory(baseDir);

        // 用户上传
        if (!string.IsNullOrEmpty(userUploadKey))
        {
            string tempFilePath = Path.Combine(IConfigBase.GetAppDataPath(), "Temp", "Uploads", userUploadKey + ".tmp");
            if (File.Exists(tempFilePath))
            {
                try
                {
                    File.Move(tempFilePath, destPath, true);
                    await report("用户核心文件部署完成。", 99.9);
                    return;
                }
                catch (Exception ex)
                {
                    await report($"部署用户文件失败: {ex.Message}", -1, true, ex);
                    throw;
                }
            }
            else
            {
                await report("上传的核心文件已过期！", -1, true);
                throw new FileNotFoundException("User uploaded core not found");
            }
        }

        // 下载核心
        if (!string.IsNullOrEmpty(downloadUrl))
        {
            bool success = await DownloadAndValidateAsync(downloadUrl, destPath, "服务端核心", sha256, report);
            if (!success) throw new Exception("Core download failed");
            await report("核心下载完成。", 99.9);
        }

        // 自动处理原版服务端依赖
        try
        {
            string targetCoreName = string.Empty;
            string targetVersion = string.Empty;

            if (coreName.Contains("-"))
            {
                string[] parts = coreName.Split('-');

                targetCoreName = parts[0];
                targetVersion = parts[^1].Replace(".jar", string.Empty);
            }

            if (!string.IsNullOrEmpty(targetCoreName) && !string.IsNullOrEmpty(targetVersion))
            {
                switch (targetCoreName.ToLower())
                {
                    case "vanilla":
                        await DownloadVanilla(Path.Combine(baseDir, ".fabric", "server"), $"{targetVersion}-server.jar",
                            targetVersion, report);
                        break;
                    case "paper":
                    case "leaves":
                    case "folia":
                    case "purpur":
                    case "leaf":
                        await DownloadVanilla(Path.Combine(baseDir, "cache"), $"mojang_{targetVersion}.jar",
                            targetVersion, report);
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            await report($"处理原版服务端依赖失败: {ex.Message}", 0);
            // 不需要抛出异常 这不影响正常安装
        }
    }

    /// 处理原版服务端的安装方法
    private async Task DownloadVanilla(string path, string filename, string version, ReportProgress report)
    {
        await report("正在下载原版服务端作为依赖···", 0);
        HttpService.HttpResponse downResponse = await MSLApi.GetAsync("/download/server/vanilla/" + version, null);
        if (downResponse.IsSuccessStatusCode)
        {
            JObject downContext = JObject.Parse(downResponse.Content!);
            string downUrl = downContext["data"]?["url"]?.ToString() ?? string.Empty;
            string sha256Exp = downContext["data"]?["sha256"]?.ToString() ?? string.Empty;

            bool success = await DownloadAndValidateAsync(downUrl, Path.Combine(path, filename), $"{version} 原版服务端",
                sha256Exp, report);
            if (!success) throw new Exception("下载原版服务端依赖失败");
            await report("原版服务端依赖下载成功。", 99.9);
        }
        else
        {
            throw new Exception("获取Vanilla端下载信息失败！");
        }
    }

    /// <summary>
    /// 安装 NeoForge/Forge
    /// 返回值: 如果安装成功并需要修改启动命令，返回新的启动参数(args)；否则返回 null
    /// </summary>
    public async Task<string?> InstallForgeIfNeededAsync(string serverId, string baseDir, string coreName,
        string javaConfig, ReportProgress report)
    {
        // 简单判断逻辑
        if (!coreName.Contains("forge") || !coreName.EndsWith(".jar") || coreName.Contains("arclight"))
        {
            return null;
        }

        _logger.LogInformation("开始 NeoForge/Forge 安装流程...");
        await report("准备运行 NeoForge/Forge 安装程序...", 0);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var installer = scope.ServiceProvider.GetRequiredService<NeoForgeInstallerService>();

            // 桥接日志
            EventHandler<NeoForgeInstallerService.InstallLogEventArgs> logHandler = async (s, e) =>
            {
                await report(e.Message, e.Progress);
            };
            installer.OnLog += logHandler;

            // 解析真实的 Java 路径
            string javaPath = javaConfig;
            if (javaConfig.Contains("MSLX://Java/"))
            {
                string version = javaConfig.Replace("MSLX://Java/", "");
                javaPath = Path.Combine(IConfigBase.GetAppDataPath(), "Tools", "Java", version, "bin",
                    PlatFormServices.GetOs() == "Windows" ? "java.exe" : "java");
            }

            (bool success, string? mcVersion) = await installer.InstallNeoForge(baseDir, coreName, javaPath);
            installer.OnLog -= logHandler;

            if (success)
            {
                await report("NeoForge/Forge 安装程序执行完毕！", 100);

                // 新版本Neoforge/Forge启动参数
                string runScript = Path.Combine(baseDir, PlatFormServices.GetOs() == "Windows" ? "run.bat" : "run.sh");
                string launchArgs = ExtractNeoForgeArgsPath(runScript) ?? string.Empty;
                if (!string.IsNullOrEmpty(launchArgs))
                {
                    return launchArgs;
                }

                // 旧版本
                string legacyJar = FindLegacyForgeJar(baseDir, mcVersion, coreName);

                if (!string.IsNullOrEmpty(legacyJar))
                {
                    return legacyJar;
                }

                throw new Exception("安装成功，但无法找到启动参数文件(run.bat)或旧版启动核心(forge-xxx.jar)！");
            }
            else
            {
                throw new Exception("NeoForge 安装器返回失败状态。");
            }
        }
        catch (Exception ex)
        {
            await report($"Forge 安装失败: {ex.Message}", -1, true, ex);
            throw;
        }
    }

    /// <summary>
    /// 部署 MCDReforged 实例：创建 server/ 布局、(可选)安装 MCDR、部署真实核心到 server/、生成 config.yml。
    /// </summary>
    public async Task DeployMcdrAsync(string serverId, McServerInfo.ServerInfo server, CreateServerRequest request,
        ReportProgress report)
    {
        await report("正在部署 MCDReforged 实例...", 5);

        string baseDir = server.Base;
        string serverDir = Path.Combine(baseDir, "server");
        Directory.CreateDirectory(serverDir);
        Directory.CreateDirectory(Path.Combine(baseDir, "plugins")); // MCDR 插件目录(与 server/plugins 不同)

        // 验证python环境
        string python = string.IsNullOrWhiteSpace(request.mcdrPython) ? "python" : request.mcdrPython.Trim();
        var (pyOk, pyVer) = await TryGetPythonVersionAsync(python);
        if (pyOk)
        {
            await report($"检测到 Python: {pyVer}", 10);
        }
        else
        {
            await report(
                $"⚠ 未能运行 Python ({python})，请确认已安装 Python 3.8+ 且可通过该命令调用。实例仍会创建，你可稍后修复后再启动。",
                10);
        }

        // 安装/更新mcdr
        if (request.mcdrInstall && pyOk)
        {
            await report("正在通过 pip 安装/更新 MCDReforged(耗时较长，请耐心等待)...", 15);
            string pipArgs = "-m pip install -U mcdreforged";
            if (!string.IsNullOrWhiteSpace(request.mcdrPipMirror))
            {
                pipArgs += $" -i {request.mcdrPipMirror.Trim()}";
            }
            if (!OperatingSystem.IsWindows())
            {
                pipArgs += " --break-system-packages";
            }

            var (pipOk, _) = await RunCommandAsync(python, pipArgs, baseDir, report, "pip", 600000);
            await report(pipOk ? "MCDReforged 安装完成。" : "⚠ MCDReforged 自动安装失败，请稍后手动执行 pip install mcdreforged。", 30);
        }

        // java
        await EnsureJavaAsync(serverId, request.java ?? "", report);

        // 服务器核心
        if (!string.IsNullOrEmpty(request.packageUrl))
        {
            string tmpKey = await DownloadPackageAsync(serverId, request.packageUrl, request.packageSha256, report);
            await DeployPackageAsync(serverId, tmpKey, null, serverDir, report);
            await ChmodBedrockServerAsync(serverId, serverDir, report);
        }

        if (!string.IsNullOrEmpty(request.packageFileKey) || !string.IsNullOrEmpty(request.packageLocalPath))
        {
            await DeployPackageAsync(serverId, request.packageFileKey, request.packageLocalPath, serverDir, report);
        }

        if (!string.IsNullOrEmpty(request.core) && request.core != "none")
        {
            await DeployCoreAsync(serverId, serverDir, request.core, request.coreFileKey, request.coreUrl,
                request.coreSha256, report);
        }

        // 启动指令
        string coreForCommand = (string.IsNullOrWhiteSpace(request.core) || request.core == "none")
            ? "server.jar"
            : request.core;

        // Forge/NeoForge 安装(在 server/ 内)
        string? forgeArgs = await InstallForgeIfNeededAsync(serverId, serverDir, request.core ?? "", request.java ?? "",
            report);
        if (!string.IsNullOrEmpty(forgeArgs))
        {
            coreForCommand = forgeArgs;
        }

        // mcdr config
        string javaToken = ResolveJavaExecPath(request.java);
        string handler = string.IsNullOrWhiteSpace(request.mcdrHandler)
            ? McdrConfigGenerator.InferHandler(request.core)
            : request.mcdrHandler.Trim();
        string startCommand =
            McdrConfigGenerator.BuildStartCommand(javaToken, request.minM, request.maxM, request.args, coreForCommand);

        var configOptions = new McdrConfigOptions
        {
            WorkingDirectory = "server",
            StartCommand = startCommand,
            Handler = handler,
            Encoding = "utf8",
            Decoding = "utf8",
            AdvancedConsole = false, // MSLX 重定向了 stdio，必须关闭
            PluginPipInstallExtraArgs = string.IsNullOrWhiteSpace(request.mcdrPipMirror)
                ? null
                : $"-i {request.mcdrPipMirror.Trim()}"
        };

        await File.WriteAllTextAsync(Path.Combine(baseDir, "config.yml"), McdrConfigGenerator.Generate(configOptions));

        string permPath = Path.Combine(baseDir, "permission.yml");
        if (!File.Exists(permPath))
        {
            await File.WriteAllTextAsync(permPath, McdrConfigGenerator.GenerateDefaultPermission());
        }

        await report($"MCDR 配置已生成 (handler: {handler})。", 95);
    }

    /// <summary>
    /// 将 MSLX 的 Java 配置解析为可执行文件路径。
    /// </summary>
    private string ResolveJavaExecPath(string? javaConfig)
    {
        if (string.IsNullOrWhiteSpace(javaConfig) || javaConfig is "java" or "none")
            return "java";

        if (javaConfig.StartsWith("MSLX://Java/"))
        {
            string version = javaConfig.Replace("MSLX://Java/", "");
            return Path.Combine(IConfigBase.GetAppDataPath(), "Tools", "Java", version, "bin",
                PlatFormServices.GetOs() == "Windows" ? "java.exe" : "java");
        }

        return javaConfig;
    }

    /// <summary>
    /// 运行 {python} --version 检测 Python 是否可用。
    /// </summary>
    private async Task<(bool ok, string version)> TryGetPythonVersionAsync(string python)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = python,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p == null) return (false, string.Empty);

            string so = await p.StandardOutput.ReadToEndAsync();
            string se = await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();

            return (p.ExitCode == 0, (so + se).Trim());
        }
        catch
        {
            return (false, string.Empty);
        }
    }

    /// <summary>
    /// 运行外部命令并将输出到进度回调。
    /// </summary>
    private async Task<(bool ok, string output)> RunCommandAsync(string fileName, string arguments, string? workingDir,
        ReportProgress report, string label, int timeoutMs = 600000)
    {
        var sb = new System.Text.StringBuilder();
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDir ?? string.Empty,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };
            psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            psi.EnvironmentVariables["PYTHONUTF8"] = "1";

            using var proc = new System.Diagnostics.Process { StartInfo = psi };
            DateTime last = DateTime.MinValue;

            void OnData(string? line)
            {
                if (line == null) return;
                lock (sb) sb.AppendLine(line);
                if ((DateTime.UtcNow - last).TotalMilliseconds > 400)
                {
                    last = DateTime.UtcNow;
                    _ = report($"[{label}] {line}", null);
                }
            }

            proc.OutputDataReceived += (_, e) => OnData(e.Data);
            proc.ErrorDataReceived += (_, e) => OnData(e.Data);

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            using var cts = new CancellationTokenSource(timeoutMs);
            try
            {
                await proc.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(true); } catch { }
                return (false, sb.ToString());
            }

            return (proc.ExitCode == 0, sb.ToString());
        }
        catch (Exception ex)
        {
            return (false, sb.ToString() + "\n" + ex.Message);
        }
    }

    // 辅助方法

    public async Task<bool> DownloadAndValidateAsync(string? url, string savePath, string itemName, string? sha256,
        ReportProgress report)
    {
        if (string.IsNullOrEmpty(url)) return false;

        var downloadOpt = new DownloadConfiguration() { ChunkCount = 8, ParallelDownload = true };
        var downloader = new DownloadService(downloadOpt);

        var tcs = new TaskCompletionSource<bool>();
        DateTime lastReport = DateTime.MinValue;

        downloader.DownloadProgressChanged += async (s, e) =>
        {
            if ((DateTime.UtcNow - lastReport).TotalMilliseconds > 1000)
            {
                lastReport = DateTime.UtcNow;
                await report(
                    $"下载 {itemName} 中... {Math.Round(e.ProgressPercentage, 2)}% ({ConvertBytesToReadable(e.AverageBytesPerSecondSpeed)}/s)",
                    e.ProgressPercentage == 100 ? 99.9 : e.ProgressPercentage);
            }
        };

        downloader.DownloadFileCompleted += async (s, e) =>
        {
            if (e.Cancelled || e.Error != null)
            {
                await report($"{itemName} 下载失败: {e.Error?.Message ?? "被取消"}", -1, true, e.Error);
                tcs.TrySetResult(false);
            }
            else
            {
                if (!string.IsNullOrEmpty(sha256) && !await FileUtils.ValidateFileSha256Async(savePath, sha256))
                {
                    await report($"{itemName} 校验失败！", -1, true);
                    try
                    {
                        File.Delete(savePath);
                    }
                    catch
                    {
                    }

                    tcs.TrySetResult(false);
                }
                else
                {
                    tcs.TrySetResult(true);
                }
            }
        };

        await downloader.DownloadFileTaskAsync(url, savePath);
        return await tcs.Task;
    }

    private string ConvertBytesToReadable(double bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int order = 0;
        while (bytes >= 1024 && order < sizes.Length - 1)
        {
            order++;
            bytes = bytes / 1024;
        }

        return $"{bytes:0.##} {sizes[order]}";
    }

    private async Task ExtractJavaSmartAsync(string archivePath, string version, string baseDir)
    {
        string finalDestDir = Path.Combine(baseDir, version);
        string tempExtractPath = Path.Combine(Path.GetTempPath(), $"MSLX_Java_{Guid.NewGuid()}");

        try
        {
            Directory.CreateDirectory(tempExtractPath);

            // 解压逻辑
            if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ZipFile.ExtractToDirectory(archivePath, tempExtractPath);
            }
            else if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            {
                using var fs = File.OpenRead(archivePath);
                using var gzip = new GZipStream(fs, CompressionMode.Decompress);
                TarFile.ExtractToDirectory(gzip, tempExtractPath, true);
            }

            // 寻找 JavaHome
            string javaExec = PlatFormServices.GetOs() == "Windows" ? "java.exe" : "java";
            string? validJavaHome = Directory.GetFiles(tempExtractPath, javaExec, SearchOption.AllDirectories)
                .Select(f => Directory.GetParent(Path.GetDirectoryName(f)!)?.FullName)
                .FirstOrDefault();

            if (string.IsNullOrEmpty(validJavaHome))
                throw new DirectoryNotFoundException($"无法在压缩包中定位到包含 {javaExec} 的 bin 目录。");

            if (Directory.Exists(finalDestDir))
            {
                RemoveReadOnlyAttributes(finalDestDir);
                Directory.Delete(finalDestDir, true);
            }

            Directory.CreateDirectory(finalDestDir);

            RemoveReadOnlyAttributes(validJavaHome);

            // 复制目录结构
            foreach (string dirPath in Directory.GetDirectories(validJavaHome, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(dirPath.Replace(validJavaHome, finalDestDir));
            }

            // 复制文件
            foreach (string newPath in Directory.GetFiles(validJavaHome, "*.*", SearchOption.AllDirectories))
            {
                File.Copy(newPath, newPath.Replace(validJavaHome, finalDestDir), true);
            }

            // Linux 权限处理
            if (PlatFormServices.GetOs() != "Windows")
            {
                try
                {
                    string binPath = Path.Combine(finalDestDir, "bin");
                    if (Directory.Exists(binPath))
                    {
                        System.Diagnostics.Process.Start("chmod", $"-R +x \"{binPath}\"").WaitForExit();
                    }
                }
                catch
                {
                }
            }
        }
        finally
        {
            // 清理临时文件
            try
            {
                // 临时文件夹也要去只读，不然删不掉
                RemoveReadOnlyAttributes(tempExtractPath);
                Directory.Delete(tempExtractPath, true);
            }
            catch
            {
            }

            try
            {
                File.Delete(archivePath);
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// 递归移除文件夹及文件的“只读”属性
    /// 解决 Linux/Docker 环境下 classes.jsa 无法被删除或覆盖的问题
    /// </summary>
    private void RemoveReadOnlyAttributes(string directoryPath)
    {
        if (!Directory.Exists(directoryPath)) return;

        var dirInfo = new DirectoryInfo(directoryPath);

        // 去掉文件夹本身的只读属性
        if ((dirInfo.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
        {
            dirInfo.Attributes &= ~FileAttributes.ReadOnly;
        }

        // 递归去掉所有文件的只读属性
        foreach (var file in dirInfo.GetFiles("*", SearchOption.AllDirectories))
        {
            if (file.IsReadOnly)
            {
                file.IsReadOnly = false;
            }
        }
    }

    public static string? ExtractNeoForgeArgsPath(string batFilePath)
    {
        if (string.IsNullOrEmpty(batFilePath) || !File.Exists(batFilePath)) return null;
        try
        {
            string batFileContent = File.ReadAllText(batFilePath);
            Match match = Regex.Match(batFileContent, @"java\s+@user_jvm_args\.txt\s+(@\S+)");
            return match.Success ? match.Groups[1].Value : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 寻找旧版 Forge 的启动核心 Jar 
    /// </summary>
    /// <param name="dirPath">服务器根目录 (_base)</param>
    /// <param name="mcVer">游戏版本，如 1.12.2 (用于过滤)</param>
    /// <param name="installerName">安装包文件名 (用于排除)</param>
    /// <returns>相对路径文件名 或 null</returns>
    public static string? FindLegacyForgeJar(string dirPath, string mcVer, string installerName)
    {
        try
        {
            DirectoryInfo directoryInfo = new DirectoryInfo(dirPath);
            if (!directoryInfo.Exists) return null;

            FileInfo[] files = directoryInfo.GetFiles();

            bool IsCandidate(FileInfo f)
            {
                return f.Name.Contains(mcVer) &&
                       f.Name.EndsWith(".jar") &&
                       f.Name.Contains("forge", StringComparison.OrdinalIgnoreCase) &&
                       f.Name != installerName &&
                       !f.Name.Contains("installer") &&
                       !f.Name.Contains("shim") && // 新版 shim
                       !f.Name.Contains("server");
            }

            foreach (FileInfo file in files)
            {
                if (IsCandidate(file) && !file.Name.Contains("universal"))
                {
                    return file.FullName.Replace(dirPath + Path.DirectorySeparatorChar, "")
                        .Replace(dirPath + "\\", ""); // 返回相对路径
                }
            }

            foreach (FileInfo file in files)
            {
                if (IsCandidate(file))
                {
                    return file.FullName.Replace(dirPath + Path.DirectorySeparatorChar, "").Replace(dirPath + "\\", "");
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}