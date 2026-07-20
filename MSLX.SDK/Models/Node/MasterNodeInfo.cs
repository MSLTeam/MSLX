using System;

namespace MSLX.SDK.Models.Node
{
    public class MasterNodeInfo
    {
        public string MasterId { get; set; } = string.Empty;

        public string NodeId { get; set; } = string.Empty;

        public string CommsKey { get; set; } = string.Empty;

        public string MasterUrl { get; set; } = string.Empty;

        public DateTime LinkedAt { get; set; } = DateTime.Now;
    }
}
