using System;

namespace MSLX.Desktop.Models
{
    internal static class ConfigStore
    {
        public static string APILink { get; set; } = "https://api.mslmc.cn/v3";
        public static string DaemonAddress { get; set; } = "http://localhost:1027";

        public static string DeviceID { get; set; } = string.Empty;
        public static string DaemonApiKey { get; set; } = string.Empty;

        public static Version Version { get; set; } = new Version(0, 0, 0, 0);
        public static Version DaemonVersion { get; set; } = new Version(0, 0, 0, 0);
    }
}
