
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
        }
    }