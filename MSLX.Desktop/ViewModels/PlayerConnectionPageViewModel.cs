using CommunityToolkit.Mvvm.ComponentModel;

namespace MSLX.Desktop.ViewModels;

public partial class PlayerConnectionPageViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _logs = string.Empty;

    [ObservableProperty]
    private bool _isTunnelRunning;

    [ObservableProperty]
    private string _statusText = "正在检查...";

    [ObservableProperty]
    private string _statusColor = "#ff9800";

    [ObservableProperty]
    private int _currentTunnelId = -1;

    [ObservableProperty]
    private string _currentTunnelName = string.Empty;
}
