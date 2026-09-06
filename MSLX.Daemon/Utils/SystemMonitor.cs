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

    // macOS 资源缓存与 P/Invoke 状态
    private static double _macTotalMemMb = 0;
    private static IntPtr _macHostPort = IntPtr.Zero;
    private ulong _prevMacUserTicks = 0;
    private ulong _prevMacSysTicks = 0;
    private ulong _prevMacIdleTicks = 0;
    private ulong _prevMacNiceTicks = 0;
    private ulong _prevMacTotalTicks = 0;

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

    // --- MacOS 实现 (Darwin Mach P/Invoke，0 子进程调用) ---
    private (double, double, double) GetMacMetrics()
    {
        double cpu = 0, totalMem = 0, usedMem = 0;
        try
        {
            // 物理总内存
            if (_macTotalMemMb <= 0)
            {
                ulong memsize = 0;
                nuint size = (nuint)sizeof(ulong);
                if (sysctlbyname("hw.memsize", out memsize, ref size, IntPtr.Zero, 0) == 0 && memsize > 0)
                {
                    _macTotalMemMb = memsize / 1024.0 / 1024.0; // 转换为 MB
                }
                else
                {
                    var gcInfo = GC.GetGCMemoryInfo();
                    _macTotalMemMb = gcInfo.TotalAvailableMemoryBytes / 1024.0 / 1024.0;
                }
            }
            totalMem = _macTotalMemMb;

            IntPtr hostPort = GetMacHostPort();

            // 内存详情
            var vmStats = new VmStatistics64();
            int vmCount = Marshal.SizeOf<VmStatistics64>() / sizeof(int);

            if (host_statistics64(hostPort, HOST_VM_INFO64, ref vmStats, ref vmCount) == 0)
            {
                // 可用页面 = free + speculative + inactive (缓存计入可用)
                ulong freePages = vmStats.free_count;
                ulong speculativePages = vmStats.speculative_count;
                ulong inactivePages = vmStats.inactive_count;
                ulong availablePages = freePages + speculativePages + inactivePages;

                int pageSize = Environment.SystemPageSize > 0 ? Environment.SystemPageSize : 4096;
                double availableMemMb = (availablePages * (double)pageSize) / 1024.0 / 1024.0;
                usedMem = Math.Max(0, totalMem - availableMemMb);
            }

            // CPU 使用率
            var cpuLoad = new HostCpuLoadInfo();
            int cpuCount = Marshal.SizeOf<HostCpuLoadInfo>() / sizeof(int);

            if (host_statistics(hostPort, HOST_CPU_LOAD_INFO, ref cpuLoad, ref cpuCount) == 0)
            {
                ulong user = cpuLoad.user;
                ulong sys = cpuLoad.system;
                ulong idle = cpuLoad.idle;
                ulong nice = cpuLoad.nice;

                if (_prevMacTotalTicks > 0)
                {
                    ulong dUser = user >= _prevMacUserTicks ? user - _prevMacUserTicks : 0;
                    ulong dSys = sys >= _prevMacSysTicks ? sys - _prevMacSysTicks : 0;
                    ulong dIdle = idle >= _prevMacIdleTicks ? idle - _prevMacIdleTicks : 0;
                    ulong dNice = nice >= _prevMacNiceTicks ? nice - _prevMacNiceTicks : 0;
                    ulong totalTicks = dUser + dSys + dIdle + dNice;

                    if (totalTicks > 0)
                    {
                        cpu = ((double)(totalTicks - dIdle) / totalTicks) * 100.0;
                    }
                }

                _prevMacUserTicks = user;
                _prevMacSysTicks = sys;
                _prevMacIdleTicks = idle;
                _prevMacNiceTicks = nice;
                _prevMacTotalTicks = user + sys + idle + nice;
            }
        }
        catch { }

        // 修正边界
        if (cpu < 0) cpu = 0;
        if (cpu > 100) cpu = 100;

        return (Math.Round(cpu, 1), Math.Round(totalMem, 1), Math.Round(usedMem, 1));
    }

    private static IntPtr GetMacHostPort()
    {
        if (_macHostPort == IntPtr.Zero)
        {
            try
            {
                _macHostPort = mach_host_self();
            }
            catch
            {
                _macHostPort = IntPtr.Zero;
            }
        }
        return _macHostPort;
    }

    // --- Darwin / Mach P/Invoke 声明 ---
    private const int HOST_CPU_LOAD_INFO = 3;
    private const int HOST_VM_INFO64 = 4;
    private const string SystemLibrary = "/usr/lib/libSystem.dylib";

    [DllImport(SystemLibrary)]
    private static extern IntPtr mach_host_self();

    [DllImport(SystemLibrary)]
    private static extern int host_statistics(IntPtr host_priv, int flavor, ref HostCpuLoadInfo host_info_out, ref int host_info_outCnt);

    [DllImport(SystemLibrary)]
    private static extern int host_statistics64(IntPtr host_priv, int flavor, ref VmStatistics64 host_info_out, ref int host_info_outCnt);

    [DllImport(SystemLibrary, CharSet = CharSet.Ansi)]
    private static extern int sysctlbyname(string name, out ulong oldp, ref nuint oldlenp, IntPtr newp, nuint newlen);

    [StructLayout(LayoutKind.Sequential)]
    private struct HostCpuLoadInfo
    {
        public uint user;
        public uint system;
        public uint idle;
        public uint nice;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VmStatistics64
    {
        public uint free_count;
        public uint active_count;
        public uint inactive_count;
        public uint wire_count;
        public ulong zero_fill_count;
        public ulong reactivations;
        public ulong pageins;
        public ulong pageouts;
        public ulong faults;
        public ulong cow_faults;
        public ulong lookups;
        public ulong hits;
        public ulong purges;
        public uint purgeable_count;
        public uint speculative_count;
        public ulong decompressions;
        public ulong compressions;
        public ulong swapins;
        public ulong swapouts;
        public uint compressor_page_count;
        public uint throttled_count;
        public uint external_page_count;
        public uint internal_page_count;
        public ulong total_uncompressed_pages_in_compressor;
    }

    private double FixCpu(double v) => Math.Round(Math.Max(0, Math.Min(100, v)), 1);
    private double FixMem(double v) => Math.Round(Math.Max(0, v), 1);
}