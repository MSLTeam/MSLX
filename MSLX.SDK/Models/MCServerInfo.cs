
using System.Text;

namespace MSLX.SDK.Models;

public class McServerInfo
{
    public class ServerInfo
    {
        public int ID { get; set; }
        public required string Name { get; set; }
        public DateTime? ExpireTime { get; set; }
        public required string Base { get; set; }
        public required string Java { get; set; }
        public required string Core { get; set; }
        public int? MinM { get; set; }
        public int? MaxM { get; set; }
        public string? Args { get; set; }
        public int ForceExitDelay { get; set; } = 10;
        public string YggdrasilApiAddr { get; set; } = "";
        public string StopCommand { get; set; } = "stop";
        public int BackupMaxCount { get; set; } = 20;
        public int BackupDelay { get; set; } = 10;
        public string BackupPath { get; set; } = "MSLX://Backup/Instance";
        public bool AllowOriginASCIIColors { get; set; } = true;
        public bool MonitorPlayers { get; set; } = true;
        public bool AutoRestart { get; set; } = false;
        public bool ForceAutoRestart { get; set; } = true;
        public bool RunOnStartup { get; set; } = false;
        public bool IgnoreEula { get; set; } = false;
        public bool ForceJvmUTF8 { get; set; } = false;
        public string InputEncoding { get; set; } = "utf-8";
        public string OutputEncoding { get; set; } = "utf-8";
        public string FileEncoding { get; set; } = "utf-8";
        public string ServerPropertiesPath { get; set; } = "server.properties";
        public string PluginsPath { get; set; } = "plugins";
        public string ModsPath { get; set; } = "mods";
        public string WorldPath { get; set; } = "world";
        public string RegionPath { get; set; } = "region";
        public string? BindFrpId { get; set; }

        // ====== Docker 字段 ======
        public string DockerImage { get; set; } = "MSLX://DockerImage/Java/25";
        public string DockerWorkingDir { get; set; } = "/mslx-data";
        public string? DockerVolumes { get; set; } // 格式: "/data:/data,/etc/timezone:/etc/timezone"
        public string? DockerEnvVars { get; set; } // 格式: "EULA=true,TZ=Asia/Shanghai"
        public string? DockerNetworkMode { get; set; } // "bridge", "host", "none" 等
        public string? DockerNetworkAlias { get; set; } // 网络别名
        public string DockerPorts { get; set; } = "255665:25565"; // 格式: "25565:25565,8080:80"
        public string? DockerExtraHosts { get; set; } // 格式: "host.mslx.internal:host-gateway,db.local:192.168.1.100"

        // 资源限制（Cgroups 映射）
        public int? DockerCpuPercentage { get; set; } // CPU使用率限制 (1-100)
        public string? DockerCpuCores { get; set; } // 指定CPU核心 (格式: "0,1" 或 "0-3")
        public int? DockerMaxMemoryMb { get; set; } // 容器最大可用内存
        public int? DockerMaxSwapMb { get; set; } // 容器最大交换内存
        public string? DockerMaxStorage { get; set; } // 容器磁盘大小限制 (仅Linux且存储驱动支持, 如 "10g")

        // 网络IO限制 (需要宿主机支持 tc 且配合特定网桥)
        public string? DockerUploadRate { get; set; } // 格式: "1mb" 或 "500kb"
        public string? DockerDownloadRate { get; set; } // 格式: "1mb"

        public string? DockerExtraArgs { get; set; } // 额外指令参数
    }
}