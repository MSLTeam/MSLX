using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using System.Collections.ObjectModel;

namespace MSLX.Desktop.Models;

public partial class InstanceModel : ObservableObject
{
    public static InstanceModel Current => _instance ??= new InstanceModel(); // 单例Model，方便全局访问
    private static InstanceModel? _instance;
    [ObservableProperty]
    private ObservableCollection<InstanceInfo> _serverList = new(); // 单例Servers，可通过单例Model访问

    public class InstanceInfo
    {
        public int ID { get; set; }
        public required string Name { get; set; }
        public required string Base { get; set; }
        public required string Java { get; set; }
        public required string Core { get; set; }
        public required int Status { get; set; } = 0;
        public required string StatusText { get; set; }
        public int? MinM { get; set; }
        public int? MaxM { get; set; }
        public string? Args { get; set; }

    }

    /// <summary>
    /// 服务端实例通用设置
    /// </summary>
    public class InstanceGeneralSettings
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Base { get; set; } = "";
        public string Java { get; set; } = "";
        public string Core { get; set; } = "";
        public int MinM { get; set; }
        public int MaxM { get; set; }
        public string Args { get; set; } = "";
        public string YggdrasilApiAddr { get; set; } = "";
        public int BackupMaxCount { get; set; }
        public int BackupDelay { get; set; }
        public string BackupPath { get; set; } = "";
        public bool AllowOriginASCIIColors {  get; set; }
        public bool MonitorPlayers { get; set; }
        public bool AutoRestart { get; set; }
        public bool ForceAutoRestart { get; set; }
        public bool RunOnStartup { get; set; }
        public bool IgnoreEula { get; set; }
        public bool ForceJvmUTF8 { get; set; }
        public string InputEncoding { get; set; } = "utf-8";
        public string OutputEncoding { get; set; } = "utf-8";
        public string FileEncoding { get; set; } = "utf-8";
    }
}

// 本地Java信息模型
public class LocalJavaListModel
{
    [JsonProperty("path")]
    public string Path { get; set; } = "";
    [JsonProperty("home")]
    public string Home { get; set; } = "";
    [JsonProperty("version")]
    public string Version { get; set; } = "";
    [JsonProperty("vendor")]
    public string Vendor { get; set; } = "";
    [JsonProperty("is64Bit")]
    public bool Is64Bit { get; set; }
}
