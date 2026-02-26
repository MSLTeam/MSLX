using Avalonia.Controls;
using Material.Icons;
using Material.Icons.Avalonia;
using MSLX.Desktop.Models;
using MSLX.Desktop.Utils;
using MSLX.Desktop.Utils.API;
using Newtonsoft.Json.Linq;
using SukiUI.Controls;
using SukiUI.Dialogs;
using SukiUI.Toasts;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace MSLX.Desktop.Views;

public partial class InstanceListPage : UserControl
{
    private Dictionary<int, SukiSideMenuItem> instancePages = new Dictionary<int, SukiSideMenuItem>();

    public InstanceListPage()
    {
        InitializeComponent();

        this.Initialized += InstanceListPage_Initialized;
    }

    private void InstanceListPage_Initialized(object? sender, EventArgs e)
    {
        _ = LoadServersList();
    }

    public static async Task LoadServersList()
    {
        try
        {
            JObject res = await DaemonAPIService.GetJsonContentAsync("/api/instance/list");
            JArray servers = (JArray)res["data"]!;

            // 使用 Select 进行投影 (Mapping)
            var serverItems = servers.Cast<JObject>().Select(server => new InstanceModel.InstanceInfo
            {
                ID = (int)server["id"]!,
                Name = (string)server["name"]!,
                Base = (string)server["basePath"]!,
                Java = (string)server["java"]!,
                Core = (string)server["core"]!,
                Status = (int)server["status"]!,
                StatusText = (string)server["statusText"]!,
            }).Reverse();
            // 直接传入构造函数，避免多次扩容
            var _list = new ObservableCollection<InstanceModel.InstanceInfo>(serverItems);

            InstanceModel.Model.ServerList = _list;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"加载服务器列表失败: {ex.Message}");
        }
    }

    // 运行服务器命令
    public void RunServer(int serverId)
    {
        var server = InstanceModel.Model.ServerList.FirstOrDefault(s => s.ID == serverId);
        if (server != null)
        {
            if (instancePages.TryGetValue(serverId, out SukiSideMenuItem? value))
            {
                SideMenuHelper.MainSideMenuHelper?.NavigateTo(value);
                return;
            }
            var serverPage = new SukiUI.Controls.SukiSideMenuItem
            {
                Header = server.Name,
                Icon = new MaterialIcon()
                {
                    Kind = MaterialIconKind.Server,
                },
                IsContentMovable = false,
                PageContent = new InstanceInfo.InstancePage(server.ID)
            };
            instancePages[serverId] = serverPage;
            SideMenuHelper.MainSideMenuHelper?.NavigateTo(serverPage, true, 2);
        }
    }

    // 删除服务器命令
    public async Task DeleteServer(int serverId)
    {
        var server = InstanceModel.Model.ServerList.FirstOrDefault(s => s.ID == serverId);
        if (server != null)
        {
            await DaemonAPIService.PostApiAsync("/api/instance/delete", new { id = serverId }, HttpService.PostContentType.Json);
            await LoadServersList();
            DialogService.ToastManager.CreateToast()
                            .OfType(Avalonia.Controls.Notifications.NotificationType.Information)
                            .WithTitle("已发送删除指令")
                            .WithContent($"已向守护程序发送服务器 {server.Name} 的删除指令！")
                            .Dismiss().After(TimeSpan.FromSeconds(5))
                            .Queue();
        }
    }

    // 打开文件夹命令
    public void OpenFolder(int serverId)
    {
        var server = InstanceModel.Model.ServerList.FirstOrDefault(s => s.ID == serverId);
        if (server != null)
        {
            Process.Start(new ProcessStartInfo(server.Base) { UseShellExecute = true });
        }
    }

    private void RunServerButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is int serverId)
        {
            RunServer(serverId);
        }
    }

    private async void DeleteServerButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is int serverId)
        {
            await DeleteServer(serverId);
        }
    }

    private void OpenFolderButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is int serverId)
        {
            OpenFolder(serverId);
        }
    }

    private void SettingsButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is int serverId)
        {
            System.Diagnostics.Debug.WriteLine($"打开服务器 {serverId} 的设置");
        }
    }

    private async void RefreshBtn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await LoadServersList();
        DialogService.ToastManager.CreateToast()
                        .OfType(Avalonia.Controls.Notifications.NotificationType.Success)
                        .WithTitle("刷新成功！")
                        .WithContent($"服务端列表已成功刷新！")
                        .Dismiss().After(TimeSpan.FromSeconds(5))
                        .Queue();
    }

    private void CreateInstance_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SideMenuHelper.MainSideMenuHelper?.NavigateTo(new SukiUI.Controls.SukiSideMenuItem
        {
            Header = "创建实例",
            Icon = new MaterialIcon()
            {
                Kind = MaterialIconKind.AddCircle,
            },
            IsContentMovable = false,
            PageContent = PageStore.CreateMCServerPage
        }, true, 2);
    }
}