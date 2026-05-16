using System;
using System.Collections.Generic;
using System.Text;

namespace MSLX.Desktop.Models
{
    public class MSLFrpModel
    {
        public class Tunnel
        {
            public required int Id { get; set; }
            public required string Name { get; set; }
            public required string Remarks { get; set; }
            public required string Status { get; set; }
            public required int LocalPort { get; set; }
            public required int RemotePort { get; set; }
            public required string Node { get; set; }
        }

        public class Node
        {
            public required int Id { get; set; }
            public required int AllowUserGroup { get; set; }
            public required string Type { get; set; }
            public required int Bandwidth { get; set; }
            public required bool HttpSupport { get; set; }
            public required bool UdpSupport { get; set; }
            public required bool KcpSupport { get; set; }
            public required int MaxOpenPort { get; set; }
            public required int MinOpenPort { get; set; }
            public required bool NeedRealName { get; set; }
            public required string Name { get; set; }
            public required string Status { get; set; }
            public required string Remarks { get; set; }
        }
    }
}
