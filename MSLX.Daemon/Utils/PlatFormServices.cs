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
    
    
    public static string GetDeviceId()
    {
        try
        {
            string platformId;
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

}