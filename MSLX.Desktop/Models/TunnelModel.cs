using System.Collections.ObjectModel;

namespace MSLX.Desktop.Models;

public class TunnelModel
{
    public static ObservableCollection<TunnelInfo> TunnelList { get; } = new();

    public class TunnelInfo
    {
        public int ID { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Service { get; set; } = string.Empty;
        public string ConfigType { get; set; } = string.Empty;
        public bool Status { get; set; }

        // 辅助属性
        public string StatusText => Status ? "运行中" : "已停止";
        public string StatusColor => Status ? "#52c41a" : "#ff4d4f";
    }
}