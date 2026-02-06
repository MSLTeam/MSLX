using Avalonia.Controls;
using Material.Icons;
using Material.Icons.Avalonia;
using MSLX.Desktop.Models;
using MSLX.Desktop.Utils;
using MSLX.Desktop.Utils.API;
using MSLX.Desktop.Views.CreateInstance;
using Newtonsoft.Json.Linq;
using SukiUI.Dialogs;
using SukiUI.Toasts;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace MSLX.Desktop.Views;

public partial class InstanceListPage : UserControl
{
    // 后端数据状态Status定义：0=未启动，1=启动中，2=运行中，3=停止中，4=重启中
    private readonly MCServerModel _model;
    public ObservableCollection<MCServerModel.ServerInfo> ServerList => _model.ServerList;

    public InstanceListPage()
    {
        InitializeComponent();

        _model = new MCServerModel();
        DataContext = this;

        this.Initialized += InstanceListPage_Initialized;
    }

    private void InstanceListPage_Initialized(object? sender, EventArgs e)
    {
        _ = LoadServersList();
    }

    public async Task LoadServersList()
    {
        try
        {
            JObject res = await DaemonAPIService.GetJsonContentAsync("/api/instance/list");
            JArray servers = (JArray)res["data"]!;
            _model.ServerList.Clear();

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                _model.ServerList.Clear();
                foreach (JObject server in servers.Cast<JObject>())
                {
                    _model.ServerList.Add(new MCServerModel.ServerInfo
                    {
                        ID = (int)server["id"]!,
                        Name = (string)server["name"]!,
                        Base = (string)server["basePath"]!,
                        Java = (string)server["java"]!,
                        Core = (string)server["core"]!,
                        Status = (int)server["status"]!,
                        StatusStr = (string)server["statusText"]!,
                    });
                }
            });
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载服务器列表失败: {ex.Message}");
        }
    }


    // 运行服务器命令
    public void RunServer(int serverId)
    {
        var server = _model.ServerList.FirstOrDefault(s => s.ID == serverId);
        if (server != null)
        {
            server.Status = 2;
            // 这里添加你的服务器启动逻辑
            System.Diagnostics.Debug.WriteLine($"服务器 {server.Name} 状态切换");
        }
    }

    // 删除服务器命令
    public void DeleteServer(int serverId)
    {
        var server = _model.ServerList.FirstOrDefault(s => s.ID == serverId);
        if (server != null)
        {
            _model.ServerList.Remove(server);
            System.Diagnostics.Debug.WriteLine($"删除服务器 {server.Name}");
        }
    }

    // 打开文件夹命令
    public void OpenFolder(int serverId)
    {
        var server = _model.ServerList.FirstOrDefault(s => s.ID == serverId);
        if (server != null)
        {
            // 这里添加打开文件夹的逻辑
            System.Diagnostics.Debug.WriteLine($"打开服务器 {server.Name} 的文件夹");
        }
    }

    private void RunServerButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is int serverId)
        {
            RunServer(serverId);
        }
    }

    private void DeleteServerButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is int serverId)
        {
            DeleteServer(serverId);
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
            PageContent = new CreateMCServer()
        }, true, 2);
    }
}