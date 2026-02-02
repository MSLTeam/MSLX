using Avalonia.Controls;
using MSLX.Desktop.Models;
using MSLX.Desktop.Utils;
using MSLX.Desktop.Utils.API;
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
    private MCServerModel _model;
    public ObservableCollection<MCServerModel.ServerInfo> ServerList => _model.ServerList;

    public InstanceListPage()
    {
        InitializeComponent();

        _model = new MCServerModel();
        DataContext = this;

        this.Loaded += InstanceListPage_Loaded;
    }

    // 页面加载后执行的内容
    private void InstanceListPage_Loaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
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
                foreach (JObject server in servers)
                {
                    _model.ServerList.Add(new MCServerModel.ServerInfo
                    {
                        ID = (int)server["id"]!,
                        Name = (string)server["name"]!,
                        Base = (string)server["basePath"]!,
                        Java = (string)server["java"]!,
                        Core = (string)server["core"]!,
                        IsRunning = (bool)server["status"]!,
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
            server.IsRunning = !server.IsRunning;
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


    private void Button_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _model.ServerList.Add(new MCServerModel.ServerInfo
        {
            ID = _model.ServerList.Count + 1,
            Name = "新服务器",
            Base = "1.20.1",
            Java = "Java 17",
            Core = "Paper",
            IsRunning = false,
            MinM = 1024,
            MaxM = 4096
        });
    }
}