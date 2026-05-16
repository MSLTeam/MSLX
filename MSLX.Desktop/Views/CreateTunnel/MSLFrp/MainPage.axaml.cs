using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using MSLX.Desktop.Utils;
using MSLX.Desktop.Utils.API;
using MSLX.Desktop.ViewModels.CreateTunnel.MSLFrp;
using Newtonsoft.Json.Linq;
using SukiUI.Dialogs;
using SukiUI.Toasts;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using static MSLX.Desktop.Models.MSLFrpModel;

namespace MSLX.Desktop.Views.CreateTunnel.MSLFrp;

public partial class MainPage : UserControl
{
    private string _userToken = string.Empty;

    private readonly Dictionary<int, string> _nodeMap = new();
    private CreateMSLFrpTunnel FatherControl {  get; set; }

    public class UserInfo
    {
        public string Username { get; set; } = string.Empty;
        public string UserGroup { get; set; } = string.Empty;
        public string UserMaxTunnels { get; set; } = string.Empty;
        public string UserOutdated { get; set; } = string.Empty;
    }

    private UserInfo _userInfo = new();

    // ViewModel 作为数据中心
    private MainPageViewModel _vm = new();

    public MainPage(CreateMSLFrpTunnel fatherControl, UserInfo userInfo)
    {
        InitializeComponent();
        _vm = (MainPageViewModel)DataContext!;
        FatherControl = fatherControl;
        _userInfo = userInfo;
        Initialized += OnInitialized;
    }

    // design time constructor
    public MainPage() : this(new CreateMSLFrpTunnel(), new UserInfo()) { }

    // 初始化
    private async void OnInitialized(object? sender, EventArgs e)
    {
        // 绑定列表数据源
        //TunnelListBox.ItemsSource = _tunnels;
        //NodeListBox.ItemsSource = _nodes;

        // 设置默认值
        CreateLocalIpBox.Text = "127.0.0.1";
        CreateLocalPortBox.Value = 25565;

        UsernameText.Text =_userInfo.Username;
        UserGroupText.Text = _userInfo.UserGroup;
        UserMaxTunnelsText.Text = _userInfo.UserMaxTunnels;
        UserOutdatedText.Text = _userInfo.UserOutdated;

        /*
        // 尝试自动登录
        var token = ConfigService.Config.ReadConfigKey("MSLUserToken")?.ToString();
        if (!string.IsNullOrEmpty(token))
        {
            _userToken = token;
            await FatherControl.GetFrpInfoAsync();
        }
        else
        {
            ShowLoginPage();
        }
        */

        await GetNodes();
        await GetTunnels();
    }

    private void ShowLoginPage()
    {
        //LoginGrid.IsVisible = true;
        MainTabControl.IsVisible = false;
    }

    private async Task GetNodes()
    {
        try
        {
            var response = await MSLUserService.GetAsync("/frp/nodeList", new Dictionary<string, string> { ["Authorization"] = $"Bearer {_userToken}" });

            if (response.IsSuccess != true || response.Content == null)
            {
                DialogService.ToastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("错误")
                .WithContent("获取节点列表失败")
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Queue();
                return;
            }

            JObject json = JObject.Parse(response.Content);
            if (json["code"]?.Value<int>() != 200)
            {
                DialogService.ToastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("获取节点列表失败")
                .WithContent(json["msg"]?.Value<string>() ?? "Err")
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Queue();
                return;
            }

            JToken? data = json["data"];
            if (data == null || !data.HasValues) return;

            _nodeMap.Clear();
            _vm.Nodes.Clear();

            foreach (var nodeItem in data)
            {
                int nodeId = nodeItem["id"]?.Value<int>() ?? 0;
                string nodeName = nodeItem["node"]?.Value<string>() ?? string.Empty;
                _nodeMap[nodeId] = nodeName;

                _vm.Nodes.Add(new Node
                {
                    Id = nodeId,
                    AllowUserGroup = nodeItem["allow_user_group"]?.Value<int>() ?? 0,
                    Type = (nodeItem["allow_user_group"]?.Value<int>() ?? 0) == 0 ? "免费"
                        : (nodeItem["allow_user_group"]?.Value<int>() ?? 1) == 1 ? "高级" : "超级",
                    Bandwidth = nodeItem["bandwidth"]?.Value<int>() ?? 0,
                    HttpSupport = (nodeItem["http_support"]?.Value<int>() ?? 0) == 1,
                    UdpSupport = (nodeItem["udp_support"]?.Value<int>() ?? 0) == 1,
                    KcpSupport = (nodeItem["kcp_support"]?.Value<int>() ?? 0) == 1,
                    MaxOpenPort = nodeItem["max_open_port"]?.Value<int>() ?? 0,
                    MinOpenPort = nodeItem["min_open_port"]?.Value<int>() ?? 0,
                    NeedRealName = (nodeItem["need_real_name"]?.Value<int>() ?? 0) == 1,
                    Name = nodeName,
                    Status = (nodeItem["status"]?.Value<int>() ?? 0) == 1 ? "在线" : "离线",
                    Remarks = nodeItem["remarks"]?.Value<string>() ?? string.Empty
                });
            }

            if (_vm.Nodes.Count > 0)
                NodeListBox.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }

    private async Task GetTunnels()
    {
        try
        {
            var response = await MSLUserService.GetAsync("/frp/getTunnelList", new Dictionary<string, string> { ["Authorization"] = $"Bearer {_userToken}" });

            if (!response.IsSuccess || response.Content == null)
            {
                DialogService.ToastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("错误")
                .WithContent("获取隧道列表失败")
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Queue();
                return;
            }

            JObject json = JObject.Parse(response.Content);
            if (json["code"]?.Value<int>() != 200)
            {
                DialogService.ToastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("获取隧道列表失败")
                .WithContent(json["msg"]?.Value<string>() ?? "Err")
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Queue();
                return;
            }

            JToken? data = json["data"];
            if (data == null || !data.HasValues) return;

            _vm.Tunnels.Clear();
            foreach (var tunnel in data)
            {
                int nodeId = tunnel["node_id"]?.Value<int>() ?? 0;
                string nodeName = _nodeMap.ContainsKey(nodeId) ? _nodeMap[nodeId] : "未知节点";
                _vm.Tunnels.Add(new Tunnel
                {
                    Id = tunnel["id"]?.Value<int>() ?? 0,
                    Name = tunnel["name"]?.Value<string>() ?? string.Empty,
                    Remarks = tunnel["remarks"]?.Value<string>() ?? string.Empty,
                    Status = (tunnel["status"]?.Value<int>() ?? 0) == 0 ? "隧道未启动" : "隧道已在线",
                    LocalPort = tunnel["local_port"]?.Value<int>() ?? 0,
                    RemotePort = tunnel["remote_port"]?.Value<int>() ?? 0,
                    Node = nodeName
                });
            }

            if (_vm.Tunnels.Count > 0)
                TunnelListBox.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }

    // 按钮事件
    private async void RefreshTunnels_Click(object? sender, RoutedEventArgs e)
        => await GetTunnels();

    private async void DeleteTunnel_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm.SelectedTunnel == null)
        {
            DialogService.ToastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("错误")
                .WithContent("请选择一条隧道！")
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Queue();
            return;
        }
        try
        {
            var response = await MSLUserService.PostAsync(
                "/frp/deleteTunnel",
                HttpService.PostContentType.Json,
                new Dictionary<string, string> { ["id"] = _vm.SelectedTunnel.Id.ToString() }
            );
            JObject json = JObject.Parse(response.Content ?? string.Empty);
            if (json["code"]?.Value<int>() == 200)
            {
                DialogService.ToastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("删除隧道")
                .WithContent("隧道删除成功！")
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Queue();
                await GetTunnels();
            }
            else
            {
                DialogService.ToastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("删除隧道失败")
                .WithContent(json["msg"]?.Value<string>() ?? "未知错误")
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Queue();
            }
        }
        catch (Exception ex)
        {
            DialogService.ToastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("删除隧道失败")
                .WithContent(ex.Message)
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Queue();
        }
    }

    private async void UseTunnel_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm.SelectedTunnel == null)
        {
            DialogService.ToastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("错误")
                .WithContent("请选择一条隧道！")
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Queue();
            return;
        }
        try
        {
            var response = await HttpService.GetAsync(
    "/frp/getTunnelConfig",
    new Dictionary<string, string> { ["id"] = _vm.SelectedTunnel.Id.ToString() },
    headers =>
    {
        headers.Authorization = new AuthenticationHeaderValue("Bearer", _userToken);
    }
);
            if (!response.IsSuccess || response.Content == null)
            {
                DialogService.ToastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("隧道配置失败")
                .WithContent("获取隧道配置失败")
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Queue();
                return;
            }
            JObject json = JObject.Parse(response.Content);
            if (json["code"]?.Value<int>() != 200)
            {
                DialogService.ToastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("隧道配置失败")
                .WithContent(json["msg"]?.Value<string>() ?? "Err")
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Queue();
                return;
            }
        }
        catch (Exception ex)
        {
            DialogService.ToastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("隧道配置失败")
                .WithContent(ex.Message)
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Queue();
        }
    }

    // 节点列表页按钮事件
    private async void RefreshNodes_Click(object? sender, RoutedEventArgs e)
        => await GetNodes();

    private async void CreateTunnel_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm.SelectedNode == null)
        {
            DialogService.ToastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("错误")
                .WithContent("请选择一个节点！")
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Queue();
            return;
        }
        try
        {
            int typeIndex = CreateTypeCombo.SelectedIndex;
            string type = typeIndex == 0 ? "tcp" : typeIndex == 1 ? "udp" : typeIndex == 2 ? "http" : "https";

            var response = await MSLUserService.PostAsync(
                "/frp/addTunnel",
                HttpService.PostContentType.Json,
                new Dictionary<string, string>
                {
                    ["name"] = CreateNameBox.Text ?? string.Empty,
                    ["local_ip"] = CreateLocalIpBox.Text ?? "127.0.0.1",
                    ["local_port"] = ((int)(CreateLocalPortBox.Value ?? 25565)).ToString(),
                    ["remote_port"] = ((int)(CreateRemotePortBox.Value ?? 0)).ToString(),
                    ["id"] = _vm.SelectedNode.Id.ToString(),
                    ["type"] = type,
                    ["remarks"] = $"Create By MSLX {Assembly.GetExecutingAssembly().GetName().Version}",
                    ["use_kcp"] = CreateKcpToggle.IsChecked == true ? "true" : "false"
                }
            );
            JObject json = JObject.Parse(response?.Content ?? string.Empty);
            if (json["code"]?.Value<int>() == 200)
            {
                DialogService.ToastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("成功")
                .WithContent("隧道创建成功！")
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Queue();
                await GetTunnels();
                MainTabControl.SelectedIndex = 0; // 切回"我的隧道"Tab
            }
            else
            {
                DialogService.ToastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("创建隧道失败")
                .WithContent(json["msg"]?.Value<string>() ?? "未知错误")
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Queue();
            }
        }
        catch (Exception ex)
        {
            DialogService.ToastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("创建隧道失败")
                .WithContent(ex.Message)
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Queue();
        }
    }

    // 退出登录
    private async void ExitLogin_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var response = await MSLUserService.GetAsync("/user/logout", new Dictionary<string, string> { ["Authorization"] = $"Bearer {_userToken}" });
            JObject json = JObject.Parse(response?.Content ?? string.Empty);
            if (json["code"]?.Value<int>() == 200)
            {
                ConfigService.Config.WriteConfigKey("MSLUserToken", string.Empty);
                DialogService.ToastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("退出登录")
                .WithContent("退出登录成功！")
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Queue();
                _userToken = string.Empty;
                ShowLoginPage();
            }
        }
        catch (Exception ex)
        {
            DialogService.ToastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("退出登录失败")
                .WithContent(ex.Message)
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Queue();
        }
    }

    private void OpenWebsite_Click(object? sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://user.mslmc.net") { UseShellExecute = true });
    }
}