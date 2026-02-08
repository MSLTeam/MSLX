using Avalonia.Controls;
using MSLX.Desktop.Models;
using MSLX.Desktop.Utils.API;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace MSLX.Desktop.Views;

public partial class TunnelListPage : UserControl
{
    private readonly TunnelModel _model;
    public ObservableCollection<TunnelModel.TunnelInfo> TunnelList => _model.TunnelList;

    public TunnelListPage()
    {
        InitializeComponent();
        _model = new TunnelModel();
        DataContext = this;

        this.Initialized += (s, e) => _ = LoadTunnelList();
    }

    public async Task LoadTunnelList()
    {
        try
        {
            // 请求接口 /api/frp/list
            JObject res = await DaemonAPIService.GetJsonContentAsync("/api/frp/list");
            if (res["code"]?.ToObject<int>() == 200)
            {
                JArray tunnels = (JArray)res["data"]!;

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _model.TunnelList.Clear();
                    foreach (JObject item in tunnels.Cast<JObject>())
                    {
                        _model.TunnelList.Add(new TunnelModel.TunnelInfo
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
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载隧道列表失败: {ex.Message}");
        }
    }

    private async void RefreshBtn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await LoadTunnelList();
    }
}