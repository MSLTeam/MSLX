using Downloader;
using MSLX.Daemon.Utils;
using MSLX.Daemon.Utils.ConfigUtils;
using Newtonsoft.Json.Linq;
using System;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace MSLX.Daemon.Services;

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
    /// 处理整合包解压与目录调整
    /// </summary>
    public async Task DeployPackageAsync(string serverId, string packageFileKey, string targetDir, ReportProgress report)
    {
        if (string.IsNullOrEmpty(packageFileKey)) return;

        string tempFilePath = Path.Combine(IConfigBase.GetAppDataPath(), "Temp", "Uploads", packageFileKey + ".tmp");

        if (!File.Exists(tempFilePath))
        {
            await report("上传的压缩包文件已过期或不存在！", -1, true);
            return;
        }

        try
        {
            _logger.LogInformation("开始解压整合包: {Key}", packageFileKey);
            await report("正在解压服务端整合包...", 10);

            // 解压
            ZipFile.ExtractToDirectory(tempFilePath, targetDir, true);

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
            try { File.Delete(tempFilePath); } catch { }
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
    /// 部署核心文件 (支持用户上传 或 远程下载)
    /// </summary>
    public async Task DeployCoreAsync(string serverId, string baseDir, string coreName, string? userUploadKey, string? downloadUrl, string? sha256, ReportProgress report)
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
                targetVersion = parts[^1].Replace(".jar",string.Empty);   
            }

            if (!string.IsNullOrEmpty(targetCoreName) && !string.IsNullOrEmpty(targetVersion))
            {
                switch(targetCoreName.ToLower())
                {
                    case "vanilla":
                        await DownloadVanilla(Path.Combine(baseDir,".fabric","server"), $"{targetVersion}-server.jar", targetVersion, report);
                        break;
                    case "paper":
                    case "leaves":
                    case "folia":
                    case "purpur":
                    case "leaf":
                        await DownloadVanilla(Path.Combine(baseDir, "cache"), $"mojang_{targetVersion}.jar", targetVersion, report);
                        break;
                }
            }

        }
        catch (Exception ex)
        {
            await report($"处理原版服务端依赖失败: {ex.Message}",0);
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

            bool success = await DownloadAndValidateAsync(downUrl, Path.Combine(path,filename), $"{version} 原版服务端", sha256Exp, report);
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
    public async Task<string?> InstallForgeIfNeededAsync(string serverId, string baseDir, string coreName, string javaConfig, ReportProgress report)
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

    // 辅助方法

    public async Task<bool> DownloadAndValidateAsync(string? url, string savePath, string itemName, string? sha256, ReportProgress report)
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
                await report($"下载 {itemName} 中... {Math.Round(e.ProgressPercentage, 2)}% ({ConvertBytesToReadable(e.AverageBytesPerSecondSpeed)}/s)", e.ProgressPercentage == 100 ? 99.9 : e.ProgressPercentage);
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
                    try { File.Delete(savePath); } catch { }
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
                catch { }
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
            catch { }

            try { File.Delete(archivePath); } catch { }
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
        catch { return null; }
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
                    return file.FullName.Replace(dirPath + Path.DirectorySeparatorChar, "").Replace(dirPath + "\\", ""); // 返回相对路径
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