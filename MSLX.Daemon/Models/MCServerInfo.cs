
using System.Text;

namespace MSLX.Daemon.Models;
    public class McServerInfo
    {
        public class ServerInfo
        {
            public int ID { get; set; }
            public required string Name { get; set; }
            public required string Base { get; set; }
            public required string Java { get; set; }
            public required string Core { get; set; }
            public int? MinM { get; set; }
            public int? MaxM { get; set; }
            public string? Args { get; set; }
            public int ForceExitDelay { get; set; } = 10;
            public string YggdrasilApiAddr { get; set; } = "";
            public int BackupMaxCount { get; set; } = 20;
            public int BackupDelay { get; set; } = 10;
            public string BackupPath { get; set; } = "MSLX://Backup/Instance";
            public bool AutoRestart { get; set; } = false;
            public bool ForceAutoRestart { get; set; } = true;
            public bool RunOnStartup { get; set; } = false;
            public bool IgnoreEula { get; set; } = false;
            public bool ForceJvmUTF8 { get; set; } = false;
            public string InputEncoding { get; set; } = "utf-8";
            public string OutputEncoding { get; set; } = "utf-8";
            public string FileEncoding { get; set; } = "utf-8";
        }
    }