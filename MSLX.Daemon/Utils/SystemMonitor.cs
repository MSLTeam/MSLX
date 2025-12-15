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
        double cpu = 0, total = 0, used = 0;
        try
        {
            // 1. 总内存
            string totalStr = RunBash("sysctl -n hw.memsize");
            if (long.TryParse(totalStr, out long tb)) total = tb / 1024.0 / 1024.0;

            // 2. 已用内存 (Pages free)
            string vm = RunBash("vm_stat");
            var fm = Regex.Match(vm, @"Pages free:\s+(\d+)\.");
            var sm = Regex.Match(vm, @"Pages speculative:\s+(\d+)\.");
            long pf = 0;
            if (fm.Success) pf += long.Parse(fm.Groups[1].Value);
            if (sm.Success) pf += long.Parse(sm.Groups[1].Value);
            // Mac页大小默认4096
            double free = (pf * 4096) / 1024.0 / 1024.0;
            used = total - free;

            // 3. CPU (top)
            // top -l 1 采样一次
            string top = RunBash("top -l 1 -n 0 | grep \"CPU usage\"");
            var im = Regex.Match(top, @"(\d+\.\d+)%\s+idle");
            if (im.Success) cpu = 100.0 - double.Parse(im.Groups[1].Value);
        }
        catch { }
        return (FixCpu(cpu), FixMem(total), FixMem(used));
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