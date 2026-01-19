using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using MSLX.Desktop.Models;
using System.Collections.ObjectModel;
using System.Linq;

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

        // test
        LoadTestData();
    }

    private void LoadTestData()
    {
        _model.ServerList.Add(new MCServerModel.ServerInfo
        {
            ID = 1,
            Name = "生存服务器",
            Base = "1.20.1",
            Java = "Java 17",
            Core = "Paper",
            IsRunning = false,
            MinM = 1024,
            MaxM = 4096
        });

        _model.ServerList.Add(new MCServerModel.ServerInfo
        {
            ID = 2,
            Name = "创造服务器",
            Base = "1.19.4",
            Java = "Java 17",
            Core = "Spigot",
            IsRunning = true,
            MinM = 2048,
            MaxM = 8192
        });
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