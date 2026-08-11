using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace MSLX.Daemon.Utils;

public class PlatFormServices
{
    private const string HomebrewFormulaName = "mslx-daemon";
    private const string DaemonExecutableName = "MSLX-Daemon";

    public static string GetOs()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "Windows";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return "MacOS";
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return "Linux";

        return "unknown";
    }

    public static string GetOsArch()
    {
        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64) return "arm64";
        if (RuntimeInformation.ProcessArchitecture == Architecture.X64) return "amd64";

        return "unknown";
    }

    #region macOS Homebrew 相关工具

    public static bool IsHomebrewInstallation()
    {
        if (!OperatingSystem.IsMacOS()) return false;

        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        if (version == null) return false;

        var architecture = RuntimeInformation.ProcessArchitecture;
        var processPath = Environment.ProcessPath;
        if (IsHomebrewInstallationPath(processPath, architecture, version)) return true;

        var appHostPath = Path.Combine(AppContext.BaseDirectory, DaemonExecutableName);
        return IsHomebrewInstallationPath(appHostPath, architecture, version);
    }

    public static bool IsHomebrewInstallationPath(
        string? executablePath,
        Architecture architecture,
        Version version)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return false;

        var expectedExecutablePath = GetHomebrewExecutablePath(architecture, version);
        var expectedLauncherPath = GetHomebrewLauncherPath(architecture, version);
        if (expectedExecutablePath == null || expectedLauncherPath == null) return false;

        try
        {
            var fullPath = Path.GetFullPath(executablePath);
            return string.Equals(fullPath, Path.GetFullPath(expectedExecutablePath), StringComparison.Ordinal) ||
                   string.Equals(fullPath, Path.GetFullPath(expectedLauncherPath), StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    public static string? GetHomebrewExecutablePath(Architecture architecture, Version version)
    {
        var versionRoot = GetHomebrewVersionRoot(architecture, version);
        return versionRoot == null
            ? null
            : Path.Combine(versionRoot, "libexec", DaemonExecutableName);
    }

    public static string? GetHomebrewLauncherPath(Architecture architecture, Version version)
    {
        var versionRoot = GetHomebrewVersionRoot(architecture, version);
        return versionRoot == null
            ? null
            : Path.Combine(versionRoot, "bin", "mslx");
    }

    public static string? GetHomebrewBrewPath(Architecture architecture)
    {
        var homebrewPrefix = GetHomebrewPrefix(architecture);
        return homebrewPrefix == null
            ? null
            : Path.Combine(homebrewPrefix, "bin", "brew");
    }

    private static string? GetHomebrewVersionRoot(Architecture architecture, Version version)
    {
        var homebrewPrefix = GetHomebrewPrefix(architecture);

        if (homebrewPrefix == null || version.Major < 0 || version.Minor < 0) return null;

        var versionDirectory = version switch
        {
            { Build: >= 0, Revision: > 0 } =>
                $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}",
            { Build: >= 0 } => $"{version.Major}.{version.Minor}.{version.Build}",
            _ => $"{version.Major}.{version.Minor}"
        };

        return Path.Combine(
            homebrewPrefix,
            "Cellar",
            HomebrewFormulaName,
            versionDirectory);
    }

    private static string? GetHomebrewPrefix(Architecture architecture)
    {
        return architecture switch
        {
            Architecture.Arm64 => "/opt/homebrew",
            Architecture.X64 => "/usr/local",
            _ => null
        };
    }
    #endregion

    public static string? GetDeviceId()
    {
        try
        {
            string platformId = "";

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // indows 获取 SID
                var currentUser = System.Security.Principal.WindowsIdentity.GetCurrent();
                platformId = currentUser.User?.Value;
            }
            else
            {
                string machineId = GetLinuxMachineId();
                string machineName = Environment.MachineName;
                string userName = Environment.UserName;
                platformId = $"{machineId}-{machineName}-{userName}";
            }

            // 格式化
            using var md5 = MD5.Create();
            byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes($"{platformId}==Ovo**#MSL#**ovO=="));
            return BitConverter.ToString(hash).Replace("-", "").ToUpper();
        }
        catch (Exception ex)
        {
            Console.WriteLine($">> 获取设备 ID 失败: {ex.Message}");
            return null;
        }
    }

    // 尝试获取 Linux 的机器 ID
    private static string GetLinuxMachineId()
    {
        try
        {
            if (File.Exists("/etc/machine-id"))
                return File.ReadAllText("/etc/machine-id").Trim();

            if (File.Exists("/var/lib/dbus/machine-id"))
                return File.ReadAllText("/var/lib/dbus/machine-id").Trim();
        }
        catch
        {
            // 忽略文件读取异常
        }

        return string.Empty;
    }


    public static string GetFormattedVersion()
    {
        var rawVersion = System.Reflection.Assembly.GetEntryAssembly()?
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (string.IsNullOrEmpty(rawVersion)) return "0.0.0";

        var parts = rawVersion.Split('+');

        if (parts.Length > 1 && parts[1].Length >= 7)
        {
            return $"{parts[0]}-{parts[1].Substring(0, 7)}";
        }

        return rawVersion;
    }
    
    // 获取指定元数据版本号
    public static string GetAssemblyMetadata(string key, string defaultValue = "1.0.0")
    {
        var assembly = Assembly.GetExecutingAssembly();

        return assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase))
            ?.Value ?? defaultValue;
    }

    // 打开浏览器
    public static void OpenBrowser(string url)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var display = Environment.GetEnvironmentVariable("DISPLAY");
                var waylandDisplay = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");

                // 没有 X11 DISPLAY & 没有 Wayland DISPLAY → 无头环境
                if (string.IsNullOrEmpty(display) && string.IsNullOrEmpty(waylandDisplay))
                {
                    Console.WriteLine(">> 当前为纯命令行环境, 跳过浏览器打开······");
                    return;
                }
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var psi = new ProcessStartInfo("xdg-open", url)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
            }

            Console.WriteLine($">> 浏览器打开地址: {url}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($">> 无法启动浏览器: {ex.Message}. 你可以手动打开: {url}");
        }
    }
}

// 进程跟踪 用于Windows下对frpc的退出处理
public static class ProcessTracker
{
    private static IntPtr _jobHandle = IntPtr.Zero;

    static ProcessTracker()
    {
        if (OperatingSystem.IsWindows())
        {
            InitWindowsJobObject();
        }
    }

    public static void Track(Process process, bool killOnClose = true)
    {
        if (process == null) return;

        if (OperatingSystem.IsWindows() && killOnClose)
        {
            if (_jobHandle != IntPtr.Zero)
            {
                AssignProcessToJobObject(_jobHandle, process.Handle);
            }
        }
    }

    #region win 原生 API 封装

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll")]
    private static extern bool SetInformationJobObject(IntPtr hJob, JobObjectInfoType infoType, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    private enum JobObjectInfoType
    {
        ExtendedLimitInformation = 9
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryLimit;
        public UIntPtr PeakJobMemoryLimit;
    }

    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    private static void InitWindowsJobObject()
    {
        _jobHandle = CreateJobObject(IntPtr.Zero, null);

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            }
        };

        int length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
        IntPtr extendedInfoPtr = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(info, extendedInfoPtr, false);
            SetInformationJobObject(_jobHandle, JobObjectInfoType.ExtendedLimitInformation, extendedInfoPtr, (uint)length);
        }
        finally
        {
            Marshal.FreeHGlobal(extendedInfoPtr);
        }
    }

    #endregion
}
