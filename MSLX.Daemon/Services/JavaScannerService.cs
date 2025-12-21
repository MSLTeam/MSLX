using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using MSLX.Daemon.Models.Instance;
using MSLX.Daemon.Utils.ConfigUtils;
using Newtonsoft.Json.Linq;

namespace MSLX.Daemon.Services;

public class JavaScannerService
{
    private readonly ILogger<JavaScannerService> _logger;

    public JavaScannerService(ILogger<JavaScannerService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 扫描 Java 环境
    /// </summary>
    /// <param name="forceRefresh">是否强制重新扫描（忽略缓存）</param>
    /// <returns></returns>
    public async Task<List<JavaInfo>> ScanJavaAsync(bool forceRefresh = false)
    {
        // 缓存读取
        if (!forceRefresh)
        {
            var cachedToken = IConfigBase.Config.ReadConfigKey("JavaCache");
            if (cachedToken != null && cachedToken.HasValues)
            {
                try
                {
                    var cachedList = cachedToken.ToObject<List<JavaInfo>>();
                    if (cachedList != null && cachedList.Count > 0)
                    {
                        _logger.LogInformation("从 Config.json 缓存加载了 {Count} 个 Java 环境。", cachedList.Count);
                        return cachedList;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("缓存的 Java 数据格式不正确，将重新扫描: {Message}", ex.Message);
                }
            }
        }

        // 开始重新扫描
        _logger.LogInformation("开始全盘扫描 Java 环境...");
        var candidates = new HashSet<string>(); // 使用 Set 去重

        // 扫描环境变量 JAVA_HOME
        var envJavaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrEmpty(envJavaHome))
        {
            candidates.Add(Path.Combine(envJavaHome, "bin", GetJavaExecName()));
        }

        // 扫描 PATH 环境变量
        ScanPath(candidates);

        // 平台特定目录扫描
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            ScanWindows(candidates);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            await ScanMacOsAsync(candidates);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            ScanLinux(candidates);
        }

        // 验证并解析版本 并发的
        var validationTasks = candidates.Select(async path => await ValidateAndParseJava(path));
        var results = await Task.WhenAll(validationTasks);

        // 过滤掉无效结果并排序
        var validJavaList = results
            .Where(x => x != null)
            .Select(x => x!) // Where 已经过滤了 null，故使用 null-forgiving operator，
            .OrderByDescending(x => x.Version)
            .ToList();

        // 写入缓存
        if (validJavaList.Count > 0)
        {
            _logger.LogInformation("扫描完成，发现 {Count} 个有效 Java 环境，已写入缓存。", validJavaList.Count);
            IConfigBase.Config.WriteConfigKey("JavaCache", JArray.FromObject(validJavaList));
        }
        else
        {
            _logger.LogInformation("扫描完成，未发现有效的 Java 环境。");
            // 清除旧的Java配置列表
            // IConfigBase.Config.WriteConfigKey("JavaCache", new JArray());
        }

        return validJavaList; // 如果为空，会返回空列表
    }

    private string GetJavaExecName() => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "java.exe" : "java";

    // --- 核心扫描逻辑 ---

    private void ScanPath(HashSet<string> candidates)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv)) return;

        var separator = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':';
        var paths = pathEnv.Split(separator, StringSplitOptions.RemoveEmptyEntries);
        var javaExec = GetJavaExecName();

        foreach (var p in paths)
        {
            try
            {
                var fullPath = Path.Combine(p, javaExec);
                if (File.Exists(fullPath)) candidates.Add(fullPath);
            }
            catch
            {
            }
        }
    }

    private void ScanWindows(HashSet<string> candidates)
    {
        // Windows 常见目录
        ScanCommonDirectories(candidates, new[]
        {
            @"C:\Program Files\Java",
            @"C:\Program Files (x86)\Java",
            @"C:\Program Files\Eclipse Adoptium",
            @"C:\Program Files\Microsoft",
            @"C:\Program Files\BellSoft",
            @"C:\Program Files\Zulu",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jdks")
        });
    }

    private async Task ScanMacOsAsync(HashSet<string> candidates)
    {
        // /usr/libexec/java_home -V
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/usr/libexec/java_home",
                Arguments = "-V",
                RedirectStandardError = true, // 输出在 stderr
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p != null)
            {
                var output = await p.StandardError.ReadToEndAsync();
                await p.WaitForExitAsync();

                // 正则匹配路径
                var matches = Regex.Matches(output, @"\s/(.+)/Home");
                foreach (Match match in matches)
                {
                    string homePath = match.Value.Trim();
                    string binPath = Path.Combine(homePath, "bin", "java");
                    if (File.Exists(binPath)) candidates.Add(binPath);
                }
            }
        }
        catch
        {
        }

        // 扫描常见目录
        ScanCommonDirectories(candidates, new[]
        {
            "/Library/Java/JavaVirtualMachines",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library/Java/JavaVirtualMachines")
        });
    }

    private void ScanLinux(HashSet<string> candidates)
    {
        ScanCommonDirectories(candidates, new[]
        {
            "/usr/lib/jvm",
            "/usr/java",
            "/opt/java",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".sdkman/candidates/java")
        });
    }

    private void ScanCommonDirectories(HashSet<string> candidates, string[] rootDirs)
    {
        var javaExec = GetJavaExecName();
        foreach (var root in rootDirs)
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                var subDirs = Directory.GetDirectories(root);
                foreach (var dir in subDirs)
                {
                    // 标准结构
                    var binPath = Path.Combine(dir, "bin", javaExec);
                    if (File.Exists(binPath)) candidates.Add(binPath);

                    // Mac结构
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        var macPath = Path.Combine(dir, "Contents", "Home", "bin", javaExec);
                        if (File.Exists(macPath)) candidates.Add(macPath);
                    }
                }
            }
            catch
            {
            }
        }
    }

    // --- 验证逻辑 ---
    private async Task<JavaInfo?> ValidateAndParseJava(string? javaPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(javaPath))
                return null;
            var fileInfo = new FileInfo(javaPath);
            if (!fileInfo.Exists)
                return null;

            string realPath = fileInfo.LinkTarget ?? javaPath;

            var startInfo = new ProcessStartInfo
            {
                FileName = realPath,
                Arguments = "-version",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return null;

            var output = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
                return null;

            // 解析版本：openjdk version "xx.x.x" 
            var versionMatch = Regex.Match(output, "version \"([^\"]+)\"");
            if (!versionMatch.Success)
                return null;

            string vendor = "Unknown";
            if (output.Contains("OpenJDK", StringComparison.OrdinalIgnoreCase))
                vendor = "OpenJDK";
            else if (output.Contains("Java(TM)", StringComparison.OrdinalIgnoreCase))
                vendor = "Oracle";
            else if (output.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
                vendor = "Microsoft";

            // 获取 JAVA_HOME (bin 的上一级)
            string? homeDir = null;
            var dirName = Path.GetDirectoryName(realPath);
            if (!string.IsNullOrEmpty(dirName))
            {
                var parentDir = Directory.GetParent(dirName);
                homeDir = parentDir?.FullName;
            }

            return new JavaInfo
            {
                Path = realPath,
                Home = homeDir,
                Version = versionMatch.Groups[1].Value,
                Vendor = vendor,
                Is64Bit = output.Contains("64-Bit") ||
                         output.Contains("amd64") ||
                         output.Contains("x86_64")
            };
        }
        catch
        {
            return null;
        }
    }
}