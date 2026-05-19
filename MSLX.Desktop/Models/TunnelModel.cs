using CommunityToolkit.Mvvm.ComponentModel;
using MSLX.Desktop.Services;

namespace MSLX.Desktop.Models;

public partial class TunnelModel : ObservableObject
{
    public int ID { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Service { get; set; } = string.Empty;
    public string ConfigType { get; set; } = string.Empty;
    public bool Status { get; set; }

    // 辅助属性
    public string StatusText => Status ? "运行中" : "已停止";
    public string StatusColor => Status ? "#52c41a" : "#ff4d4f";

    [ObservableProperty]
    private string _logs = string.Empty;
    // public FrpTunnelSignalRService? SignalRService { get; set; } // 注入 SignalR 服务实例以接收日志，目前暂无计划使用，后续若使用独立窗口显示日志则可用上
}