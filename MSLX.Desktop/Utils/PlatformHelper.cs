using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace MSLX.Desktop.Utils
{
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

        #region Linux/macOS 获取用户 ID
        [LibraryImport("libc", EntryPoint = "getuid")]
        private static partial uint Getuid();

        private static string GetUnixUserId()
        {
            try
            {
                return Getuid().ToString();
            }
            catch
            {
                return "unknown";
            }
        }
        #endregion
    }
}
