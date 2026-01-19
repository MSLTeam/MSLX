using System;
using System.Collections.Generic;
using System.Text;

namespace MSLX.Desktop.Models
{
    internal static class ConfigStore
    {
        public static string APILink { get; set; }= "https://api.mslmc.cn/v3";
        public static string DaemonLink { get; set; }= "http://localhost:1027/api";

        public static string DeviceID { get; set; }= string.Empty;
        public static string DaemonApiKey { get; set; }= string.Empty;
    }
}
