using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using MSLX.SDK.IServices;
using MSLX.SDK.Models.Instance;

namespace MSLX.Daemon.Services;

/// <summary>
/// 扫描本机 Python 环境，供 MCDReforged 部署使用。
/// </summary>
public class PythonScannerService : IPythonScannerService
{
    private readonly ILogger<IPythonScannerService> _logger;

    private static readonly Regex VersionRegex = new(@"Python\s+([0-9]+\.[0-9]+\.[0-9]+)", RegexOptions.Compiled);

    public PythonScannerService(ILogger<IPythonScannerService> logger)
    {
        _logger = logger;
    }

    public async Task<List<PythonInfo>> ScanPythonAsync(bool forceRefresh = false)
    {
        _logger.LogInformation("开始扫描 Python 环境...");

        // 常见命令 + PATH 中的可执行文件
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "python", "python3"
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            candidates.Add("py");
        }

        ScanPath(candidates);

        // 校验并解析(去重后按可执行路径)
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<PythonInfo>();

        foreach (var cand in candidates)
        {
            var info = await InspectPythonAsync(cand);
            if (info == null) continue;

            // 用真实解析出的路径去重(python 与 python3 可能指向同一个)
            var key = string.IsNullOrEmpty(info.Version) ? info.Path : info.Version + "|" + info.Path;
            if (seen.Add(key))
            {
                results.Add(info);
            }
        }

        _logger.LogInformation("Python 扫描完成，发现 {Count} 个环境。", results.Count);
        return results
            .OrderByDescending(x => x.HasMcdr)
            .ThenByDescending(x => x.Version)
            .ToList();
    }

    public async Task<PythonInfo?> InspectPythonAsync(string python)
    {
        if (string.IsNullOrWhiteSpace(python)) return null;

        try
        {
            var (verOk, versionOut) = await RunAsync(python, "--version");
            if (!verOk) return null;

            var match = VersionRegex.Match(versionOut);
            var version = match.Success ? match.Groups[1].Value : versionOut.Replace("Python", "").Trim();

            // 检测 MCDR 是否安装
            bool hasMcdr = false;
            string? mcdrVersion = null;
            var (mcdrOk, mcdrOut) = await RunAsync(python, "-m mcdreforged --version");
            if (mcdrOk)
            {
                hasMcdr = true;
                // 输出形如 "MCDReforged v2.14.5"
                var m = Regex.Match(mcdrOut, @"v?([0-9]+\.[0-9]+(\.[0-9]+)?)");
                mcdrVersion = m.Success ? m.Groups[1].Value : mcdrOut.Trim();
            }

            return new PythonInfo
            {
                Path = python,
                Version = version,
                HasMcdr = hasMcdr,
                McdrVersion = mcdrVersion
            };
        }
        catch
        {
            return null;
        }
    }

    private void ScanPath(HashSet<string> candidates)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv)) return;

        var separator = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':';
        var isWin = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var execNames = isWin
            ? new[] { "python.exe", "python3.exe" }
            : new[] { "python3", "python" };

        foreach (var p in pathEnv.Split(separator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var exec in execNames)
            {
                try
                {
                    var fullPath = Path.Combine(p, exec);
                    if (File.Exists(fullPath)) candidates.Add(fullPath);
                }
                catch
                {
                }
            }
        }
    }

    private async Task<(bool ok, string output)> RunAsync(string fileName, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

            using var p = Process.Start(psi);
            if (p == null) return (false, string.Empty);

            var so = await p.StandardOutput.ReadToEndAsync();
            var se = await p.StandardError.ReadToEndAsync();

            using var cts = new CancellationTokenSource(5000);
            try
            {
                await p.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { p.Kill(true); } catch { }
                return (false, string.Empty);
            }

            // --version 可能输出到 stdout 或 stderr
            return (p.ExitCode == 0, (so + se).Trim());
        }
        catch
        {
            return (false, string.Empty);
        }
    }
}
