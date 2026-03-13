using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace MSLX.Daemon.Utils;

public class PlatFormServices
{
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
                // Linux/macOS：优先尝试获取系统级别的 machine-id
                platformId = GetLinuxMachineId();

                // 回退机器名 + MAC地址 作为指纹
                if (string.IsNullOrEmpty(platformId))
                {
                    string macAddress = GetMacAddress();
                    platformId = $"{Environment.MachineName}-{macAddress}";
                }
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

    // 获取MAC地址
    private static string GetMacAddress()
    {
        try
        {
            var activeMac = NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up
                              && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Select(nic => nic.GetPhysicalAddress().ToString())
                .FirstOrDefault(mac => !string.IsNullOrEmpty(mac));

            return activeMac ?? "NOMAC";
        }
        catch
        {
            return "NOMAC";
        }
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

    // 打开浏览器
    public static void OpenBrowser(string url)
    {
        try
        {
            // 检查GUI环境
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform
                    .Linux))
            {
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
                {
                    Console.WriteLine(">> Detected headless environment (no GUI). Browser auto-open skipped.");
                    return;
                }
            }

            // 尝试打开浏览器
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform
                    .Windows))
            {
                // Windows: 使用 shell 打开
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
            else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices
                         .OSPlatform.Linux))
            {
                // Linux: 使用 xdg-open
                System.Diagnostics.Process.Start("xdg-open", url);
            }
            else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices
                         .OSPlatform.OSX))
            {
                // Mac: 使用 open
                System.Diagnostics.Process.Start("open", url);
            }

            Console.WriteLine($">> 浏览器打开地址: {url}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($">> 无法启动浏览器: {ex.Message}. 你可以手动打开: {url}");
        }
    }
}