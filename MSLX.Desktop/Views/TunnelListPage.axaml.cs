using Avalonia.Controls;
using Avalonia.Interactivity;
using MSLX.Desktop.Models;
using MSLX.Desktop.Services;
using MSLX.Desktop.Utils;
using MSLX.Desktop.Utils.API;
using MSLX.Desktop.ViewModels;
using MSLX.Desktop.Views.CreateTunnel.MSLFrp;
using Newtonsoft.Json.Linq;
using SukiUI.Dialogs;
using SukiUI.Toasts;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace MSLX.Desktop.Views;

public partial class TunnelListPage : UserControl
{
    private TunnelListPageViewModel _vm = new();
    private readonly FrpTunnelSignalRService _signalR = FrpTunnelSignalRService.Shared;
    private int CurrentTunnelID = -1; // 当前查看日志的隧道ID

    public TunnelListPage()
    {
        InitializeComponent();
        _vm = (TunnelListPageViewModel)this.DataContext!;
        _signalR.LogReceived += OnLogReceived;

        this.Initialized += async (s, e) =>
        {
            await _signalR.ConnectAsync(); // 建立唯一连接
        };
        this.Loaded += async (s, e) =>
        {
            await LoadTunnelList();
        };
    }

    public async Task LoadTunnelList()
    {
        try
        {
            JObject res = await DaemonAPIService.GetJsonContentAsync("/api/frp/list");
            JArray tunnels = (JArray)res["data"]!;

            _vm.Tunnels.Clear();

            foreach (JObject item in tunnels.Cast<JObject>())
            {
                _vm.Tunnels.Add(new TunnelModel
                {
                    ID = (int)item["id"]!,
                    Name = (string)item["name"]!,
                    Service = (string)item["service"]!,
                    ConfigType = (string)item["configType"]!,
                    Status = (bool)item["status"]!
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"隧道列表加载失败: {ex.Message}");
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
                                .WithActionButton("关闭", _ => { }, true)
                                .Queue();
    }

    private async void AddBtn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (SideMenuHelper.Current.SideMenu.Items.Contains(PageStore.CreateMSLFrpTunnelMenuItem))
        {
            SideMenuHelper.Current.NavigateTo<CreateMSLFrpTunnel>();
            return;
        }
        SideMenuHelper.Current?.NavigateTo(PageStore.CreateMSLFrpTunnelMenuItem, true, SideMenuHelper.Current.SideMenu.Items.Count - 3);
    }

    private async void RunTunnel_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is int tunnelId)
        {
            try
            {
                var response = await DaemonAPIService.PostApiAsync("/api/frp/action", null, HttpService.PostContentType.Json, new { id = tunnelId, action = "start" });
                if (response.IsSuccess)
                {
                    DialogService.ToastManager.CreateToast()
                        .OfType(Avalonia.Controls.Notifications.NotificationType.Success)
                        .WithTitle("启动成功！")
                        .WithContent("隧道启动成功！连接地址请在日志中查看。")
                        .Dismiss().After(TimeSpan.FromSeconds(5))
                        .WithActionButton("关闭", _ => { }, true)
                        .Queue();
                    await LoadTunnelList();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"启动隧道失败: {ex.Message}");
                DialogService.ToastManager.CreateToast()
                                        .OfType(Avalonia.Controls.Notifications.NotificationType.Error)
                                        .WithTitle("启动失败！")
                                        .WithContent($"启动隧道时发生错误: {ex.Message}")
                                        .Dismiss().After(TimeSpan.FromSeconds(5))
                                        .WithActionButton("关闭", _ => { }, true)
                                        .Queue();
            }
        }
    }

    private async void StopTunnel_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is int tunnelId)
        {
            try
            {
                var response = await DaemonAPIService.PostApiAsync("/api/frp/action",null,HttpService.PostContentType.Json, new { id = tunnelId, action = "stop" });
                if (response.IsSuccess)
                {
                    DialogService.ToastManager.CreateToast()
                        .OfType(Avalonia.Controls.Notifications.NotificationType.Success)
                        .WithTitle("停止成功！")
                        .WithContent("隧道停止成功！")
                        .Dismiss().After(TimeSpan.FromSeconds(5))
                        .WithActionButton("关闭", _ => { }, true)
                        .Queue();
                    await LoadTunnelList();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"停止隧道失败: {ex.Message}");
                DialogService.ToastManager.CreateToast()
                                        .OfType(Avalonia.Controls.Notifications.NotificationType.Error)
                                        .WithTitle("停止失败！")
                                        .WithContent($"停止隧道时发生错误: {ex.Message}")
                                        .Dismiss().After(TimeSpan.FromSeconds(5))
                                        .WithActionButton("关闭", _ => { }, true)
                                        .Queue();
            }
        }
    }

    private async void TunnelLogs_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not int tunnelId) return;

        var tunnel = _vm.Tunnels.FirstOrDefault(t => t.ID == tunnelId);
        if (tunnel == null) return;
        CurrentTunnelID = tunnel.ID;
        // 打开弹窗前 JoinGroup
        await _signalR.JoinGroupAsync(tunnelId);

        var textBlock = new TextBlock
        {
            [!TextBlock.TextProperty] = new Avalonia.Data.Binding(nameof(tunnel.Logs)) { Source = tunnel },
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        };

        DialogService.DialogManager.CreateDialog()
            .WithTitle($"隧道日志 - {tunnel.Name}")
            .WithContent(new ScrollViewer { Content = textBlock })
            .WithActionButton("关闭", async _ =>
            {
                // 关闭弹窗时 LeaveGroup
                await _signalR.LeaveGroupAsync(tunnelId);
                CurrentTunnelID = -1;
                tunnel.Logs = string.Empty; // 清空日志内容，避免下次打开时看到旧日志
            }, true)
            .TryShow();
    }

    #region SignalR

    private void OnLogReceived(string log)
    {
        if(CurrentTunnelID == -1) return; // 没有正在查看日志的隧道
        var tunnel = _vm.Tunnels.FirstOrDefault(t => t.ID == CurrentTunnelID);
        tunnel?.Logs += log + Environment.NewLine;
    }

    #endregion
}