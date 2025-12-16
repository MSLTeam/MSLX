using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace MSLX.Daemon.Utils;

public class SystemMonitor
{
    private readonly bool _isWindows;
    private readonly bool _isLinux;
    private readonly bool _isOsX;

    // Windows 计数器
    private PerformanceCounter? _cpuCounter;
    private PerformanceCounter? _ramCounter;

    // Linux CPU 计算缓存
    private long _prevTotalTicks = 0;
    private long _prevIdleTicks = 0;

    public SystemMonitor()
    {
        _isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        _isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        _isOsX = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        if (_isWindows) InitWindows();
    }

    private void InitWindows()
    {
        try
        {
            // 初始化 Windows 计数器
            // 某些精简版 Windows 可能缺失计数器库，需 try-catch
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _ramCounter = new PerformanceCounter("Memory", "Available MBytes");
            _cpuCounter.NextValue(); // 第一次调用通常为0，预热
        }
        catch 
        { 
            // 记录日志或忽略 
        }
    }

    /// <summary>
    /// 异步获取系统资源快照
    /// </summary>
    public async Task<(double cpu, double totalMem, double usedMem)> GetStatusAsync()
    {
        // 放入 Task.Run 防止 Linux/Mac 的文件读取或命令执行阻塞 SignalR 的主线程
        return await Task.Run(() =>
        {
            if (_isWindows) return GetWindowsMetrics();
            if (_isLinux) return GetLinuxMetrics();
            if (_isOsX) return GetMacMetrics();
            return (0, 0, 0);
        });
    }

    // --- Windows 实现 ---
    private (double, double, double) GetWindowsMetrics()
    {
        double cpu = 0, avail = 0, total = 0;
        try
        {
            if (_cpuCounter != null) cpu = _cpuCounter.NextValue();
            if (_ramCounter != null) avail = _ramCounter.NextValue();
            
            // 获取物理总内存 (需要 .NET 5+)
            var gcInfo = GC.GetGCMemoryInfo();
            total = gcInfo.TotalAvailableMemoryBytes / 1024.0 / 1024.0;
        }
        catch { }
        return (FixCpu(cpu), FixMem(total), FixMem(total - avail));
    }

    // --- Linux 实现 (/proc) ---
    private (double, double, double) GetLinuxMetrics()
    {
        double cpu = 0, total = 0, avail = 0;
        try
        {
            // 1. 内存
            string memInfo = File.ReadAllText("/proc/meminfo");
            var tm = Regex.Match(memInfo, @"MemTotal:\s+(\d+)\s+kB");
            var am = Regex.Match(memInfo, @"MemAvailable:\s+(\d+)\s+kB");
            if (tm.Success) total = long.Parse(tm.Groups[1].Value) / 1024.0;
            if (am.Success) avail = long.Parse(am.Groups[1].Value) / 1024.0;

            // 2. CPU
            var lines = File.ReadAllLines("/proc/stat");
            var cpuLine = lines.FirstOrDefault(l => l.StartsWith("cpu "));
            if (cpuLine != null)
            {
                var p = cpuLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                long idle = long.Parse(p[4]);
                long t = 0;
                for (int i = 1; i < p.Length; i++) if (long.TryParse(p[i], out long v)) t += v;

                long dT = t - _prevTotalTicks;
                long dI = idle - _prevIdleTicks;
                if (dT > 0) cpu = (1.0 - ((double)dI / dT)) * 100;

                _prevTotalTicks = t; 
                _prevIdleTicks = idle;
            }
        }
        catch { }
        return (FixCpu(cpu), FixMem(total), FixMem(total - avail));
    }

    // --- MacOS 实现 (Bash Cmd) ---
    private (double, double, double) GetMacMetrics()
    {
        double cpu = 0, totalMem = 0, usedMem = 0;
        try
        {
            // 获取总内存 (sysctl)
            string totalMemStr = RunBash("sysctl -n hw.memsize");
            if (long.TryParse(totalMemStr, out long totalBytes))
            {
                totalMem = totalBytes / 1024.0 / 1024.0; // 转换为 MB
            }

            // 获取内存详情 (vm_stat)
            string vmStat = RunBash("vm_stat");
            
            // 提取关键指标
            var freeMatch = Regex.Match(vmStat, @"Pages free:\s+(\d+)\.");
            var speculativeMatch = Regex.Match(vmStat, @"Pages speculative:\s+(\d+)\."); 
            var inactiveMatch = Regex.Match(vmStat, @"Pages inactive:\s+(\d+)\."); // 这是文件缓存

            long pagesFree = 0;
            if (freeMatch.Success) pagesFree += long.Parse(freeMatch.Groups[1].Value);
            if (speculativeMatch.Success) pagesFree += long.Parse(speculativeMatch.Groups[1].Value);
            
            // 把 Inactive (缓存) 也算作“可用内存”，不计入“已用”
            if (inactiveMatch.Success) pagesFree += long.Parse(inactiveMatch.Groups[1].Value);

            // Mac 页大小通常是 4096 字节
            // 可用内存 (MB)
            double availableMemMb = (pagesFree * 4096) / 1024.0 / 1024.0;
            
            // 已用内存 = 总内存 - 可用内存
            // 这样算出来的数值就约等于 Activity Monitor 里的 "内存已用" (App + 联动 + 被压缩)
            usedMem = totalMem - availableMemMb;

            // 获取 CPU (top)
            string topOutput = RunBash("top -l 1 -n 0 | grep \"CPU usage\"");
            var idleMatch = Regex.Match(topOutput, @"(\d+\.\d+)%\s+idle");
            
            if (idleMatch.Success)
            {
                double idlePercent = double.Parse(idleMatch.Groups[1].Value);
                cpu = 100.0 - idlePercent;
            }
        }
        catch { }

        // 修正边界
        if (cpu < 0) cpu = 0;
        if (cpu > 100) cpu = 100;

        return (Math.Round(cpu, 1), Math.Round(totalMem, 1), Math.Round(usedMem, 1));
    }

    private string RunBash(string cmd)
    {
        try
        {
            using var p = new Process();
            p.StartInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash", Arguments = $"-c \"{cmd}\"",
                RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true
            };
            p.Start();
            string r = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return r;
        }
        catch { return ""; }
    }

    private double FixCpu(double v) => Math.Round(Math.Max(0, Math.Min(100, v)), 1);
    private double FixMem(double v) => Math.Round(Math.Max(0, v), 1);
}