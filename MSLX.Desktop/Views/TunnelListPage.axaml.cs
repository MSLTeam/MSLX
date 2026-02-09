using Avalonia.Controls;
using MSLX.Desktop.Models;
using MSLX.Desktop.Utils;
using MSLX.Desktop.Utils.API;
using Newtonsoft.Json.Linq;
using SukiUI.Toasts;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace MSLX.Desktop.Views;

public partial class TunnelListPage : UserControl
{
    public ObservableCollection<TunnelModel.TunnelInfo> Tunnels => TunnelModel.TunnelList;

    public TunnelListPage()
    {
        InitializeComponent();
        
        this.Initialized += (s, e) => _ = LoadTunnelList();
    }

    public async Task LoadTunnelList()
    {
        try
        {
            JObject res = await DaemonAPIService.GetJsonContentAsync("/api/frp/list");
            JArray tunnels = (JArray)res["data"]!;

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                TunnelModel.TunnelList.Clear();
                foreach (JObject item in tunnels.Cast<JObject>())
                {
                    TunnelModel.TunnelList.Add(new TunnelModel.TunnelInfo
                    {
                        ID = (int)item["id"]!,
                        Name = (string)item["name"]!,
                        Service = (string)item["service"]!,
                        ConfigType = (string)item["configType"]!,
                        Status = (bool)item["status"]!
                    });
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"隧道列表加载失败: {ex.Message}");
        }
    }

    private async void RefreshBtn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await LoadTunnelList();
        DialogService.ToastManager.CreateToast()
                                .OfType(Avalonia.Controls.Notifications.NotificationType.Success)
                                .WithTitle("刷新成功！")
                                .WithContent($"隧道列表已成功刷新！")
                                .Dismiss().After(TimeSpan.FromSeconds(5))
                                .Queue();
    }
}