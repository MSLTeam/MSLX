using System;

namespace MSLX.SDK.Models.Node
{
    public class SlaveNodeConfig
    {
        public string NodeId { get; set; } = string.Empty;

        public string NodeName { get; set; } = string.Empty;

        public string NodeUrl { get; set; } = string.Empty;

        public string NodeLogo { get; set; } = "https://www.mslmc.cn/logo.png";

        public string MasterUrl { get; set; } = string.Empty;

        public string LinkKey { get; set; } = string.Empty;

        public string CommsKey { get; set; } = string.Empty;

        public string NodeTags { get; set; } = string.Empty;

        public DateTime LinkedAt { get; set; } = DateTime.Now;
    }
}
