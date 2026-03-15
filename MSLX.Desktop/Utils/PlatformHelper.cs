using MSLX.Desktop.Models;
using System;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace MSLX.Desktop.Utils;

internal partial class PlatformHelper
{
    public enum TheArchitecture
    {
        X86,
        X64,
        Arm,
        Arm64,
        Unknown
    }

    public enum TheOSPlatform
    {
        Windows,
        Linux,
        OSX,
        Unknown
    }

    public static TheOSPlatform GetOS()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? TheOSPlatform.Windows :
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? TheOSPlatform.OSX :
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? TheOSPlatform.Linux :
            TheOSPlatform.Unknown;
    }

    public static TheArchitecture GetOSArch()
    {
        return RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => TheArchitecture.X64,
            Architecture.X86 => TheArchitecture.X86,
            Architecture.Arm => TheArchitecture.Arm,
            Architecture.Arm64 => TheArchitecture.Arm64,
            _ => TheArchitecture.Unknown,
        };
    }


    public static string? GetDeviceID()
    {
        try
        {
            string? platformId = null;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows 获取 SID
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

            // 生成 MD5，使用相同的盐值
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

    public static bool IsLocalService()
    {
        var addr = ConfigStore.DaemonAddress?.ToLower() ?? string.Empty;

        if (addr.Contains("localhost") ||
            addr.Contains("127.0.0.1") ||
            addr.Contains("[::1]"))
        {
            return true;
        }

        return false;
    }
}