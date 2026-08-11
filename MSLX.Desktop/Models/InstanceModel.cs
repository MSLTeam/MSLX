using CommunityToolkit.Mvvm.ComponentModel;
using MSLX.SDK.Models;
using System.Collections.ObjectModel;

namespace MSLX.Desktop.Models;

public partial class InstanceModel : ObservableObject
{
    public static InstanceModel Current => _instance ??= new InstanceModel(); // 单例Model，方便全局访问
    private static InstanceModel? _instance;
    [ObservableProperty]
    private ObservableCollection<InstanceInfo> _serverList = new(); // 单例Servers，可通过单例Model访问

    public class InstanceInfo : McServerInfo.ServerInfo
    {
        public int Status { get; set; } = 0;
        public string StatusText { get; set; } = "";
    }
}
