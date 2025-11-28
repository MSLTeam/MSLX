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
            string? platformId;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows 获取 SID
                var currentUser = System.Security.Principal.WindowsIdentity.GetCurrent();
                platformId = currentUser.User?.Value;
            }
            else
            {
                // Linux/macOS 组合标识
                var userId = GetUnixUserId();
                var userName = Environment.GetEnvironmentVariable("USER");
                var homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                platformId = $"{userId}-{userName}-{homePath}";
            }

            // 生成 MD5
            using var md5 = MD5.Create();
            byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes($"{platformId}==Ovo**#MSL#**ovO=="));
            return BitConverter.ToString(hash).Replace("-", "").ToUpper();
        }
        catch
        {
            return null;
        }
    }

    // Linux/macOS 获取用户 ID
    [DllImport("libc")]
    private static extern uint getuid();

    private static string GetUnixUserId()
    {
        try
        {
            return getuid().ToString();
        }
        catch
        {
            return "unknown";
        }
    }
    
    // 打开浏览器
    public static void OpenBrowser(string url)
    {
        try
        {
            // 检查GUI环境
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
            {
                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
                {
                    Console.WriteLine(">> Detected headless environment (no GUI). Browser auto-open skipped.");
                    return;
                }
            }

            // 尝试打开浏览器
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                // Windows: 使用 shell 打开
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
            else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
            {
                // Linux: 使用 xdg-open
                System.Diagnostics.Process.Start("xdg-open", url);
            }
            else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
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