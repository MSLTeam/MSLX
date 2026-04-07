using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using MSLX.Desktop.Utils;
using MSLX.Desktop.Utils.API;
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

public partial class CreateMSLFrpTunnel : UserControl
{
    private string _userToken = string.Empty;
    private readonly ObservableCollection<Tunnel> _tunnels = new();
    private readonly ObservableCollection<Node> _nodes = new();
    private readonly Dictionary<int, string> _nodeMap = new();

    private Tunnel? _selectedTunnel;
    private Node? _selectedNode;

    public CreateMSLFrpTunnel()
    {
        InitializeComponent();
        Initialized += OnInitialized;
    }

    // 初始化
    private async void OnInitialized(object? sender, EventArgs e)
    {
        // 绑定列表数据源
        TunnelListBox.ItemsSource = _tunnels;
        NodeListBox.ItemsSource = _nodes;

        // 设置默认值
        CreateLocalIpBox.Text = "127.0.0.1";
        CreateLocalPortBox.Value = 25565;

        // 尝试自动登录
        var token = ConfigService.Config.ReadConfigKey("MSLUserToken")?.ToString();
        if (!string.IsNullOrEmpty(token))
        {
            _userToken = token;
            await GetFrpInfoAsync();
        }
        else
        {
            ShowLoginPage();
        }
    }

    // 页面切换
    private void ShowLoginPage()
    {
        LoginGrid.IsVisible = true;
        MainTabControl.IsVisible = false;
    }

    private void ShowMainPage()
    {
        LoginGrid.IsVisible = false;
        MainTabControl.IsVisible = true;
    }

    // 登录
    private async void LoginButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            DialogService.DialogManager.CreateDialog()
                .WithTitle("登录中")
                .WithContent("正在登录，请稍候...")
                .TryShow();

            Random rand = new Random();
            string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            string randomString = string.Empty;

            for (int i = 0; i < 32; i++)
            {
                randomString += chars[rand.Next(chars.Length)];
            }
            string csrf = randomString;
            var postData = new Dictionary<string, string>
            {
                { "csrf", csrf },
                { "appid", "eixl7BLlidSZ7POjdhZsAGTXKyu" }
            };

            var response = await MSLUserService.PostAsync(
                "/oauth/createAppLogin",
                HttpService.PostContentType.FormUrlEncoded,
                postData
            );

            if (response.IsSuccess&& response.Content != null)
            {
                JObject _json = JObject.Parse(response.Content);
                if (_json["code"]?.Value<int>() != 200 || _json["data"] == null)
                {
                    return;
                }
                JObject data = _json["data"] as JObject ?? new JObject();
                string url = data["url"]?.Value<string>() ?? string.Empty;
                string ssid = data["ssid"]?.Value<string>() ?? string.Empty;

                if (string.IsNullOrEmpty(url) && string.IsNullOrEmpty(ssid))
                {
                    return;
                }
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                StartPolling(ssid, csrf); // 轮询
            }
            else
            {
                return;
            }

            DialogService.DialogManager.DismissDialog();
            
            JObject json = JObject.Parse(response.Content);
            if (json["code"]?.Value<int>() == 200)
            {
                _userToken = json["data"]?["token"]?.Value<string>() ?? string.Empty;

                if (SaveLoginToggle.IsChecked == true)
                    ConfigService.Config.WriteConfigKey("MSLUserToken", _userToken);

                await GetFrpInfoAsync();
            }
            else
            {
                DialogService.ToastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("登录失败")
                .WithContent(json["msg"]?.Value<string>() ?? "未知错误！")
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Queue();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
            DialogService.DialogManager.DismissDialog();
        }
    }

    private CancellationTokenSource _pollingCts; // 取消轮询
    private async void StartPolling(string ssid, string csrf)
    {
        _pollingCts = new CancellationTokenSource();
        var cancellationToken = _pollingCts.Token;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var response = await MSLUserService.GetAsync(
                    $"/oauth/appLogin?ssid={ssid}&csrf={csrf}",null
                    
                );

                if (cancellationToken.IsCancellationRequested) return;
                JObject ContentInfo = JObject.Parse(response.Content);
                if (response.IsSuccess)
                {
                    var appToken = ContentInfo?["token"]?.Value<string>();
                    if (!string.IsNullOrEmpty(appToken))
                    {
                        await CompleteBrowserLogin(appToken);
                        return; // 结束轮询
                    }
                    else
                    {
                        // 继续轮询

                    }
                }
                else
                {
                    // 出现错误
                    // 取消轮询
                    _pollingCts?.Cancel();
                    return; // 结束轮询
                }

                // 延迟
                await Task.Delay(3000, cancellationToken);
            }
        }
        catch (TaskCanceledException)
        {

        }
        catch (Exception ex)
        {
            // 取消轮询
            _pollingCts?.Cancel();
        }
        finally
        {
            if (_pollingCts != null)
            {
                _pollingCts.Dispose();
                _pollingCts = null;
            }
        }
    }

    private async Task CompleteBrowserLogin(string appToken)
    {
        var (Code, Msg, ContentInfo) = await UserLogin(
            appToken,
            string.Empty, // email
            string.Empty, // password
            string.Empty, // auth2FA
            false
        );

        if (Code == 200)
        {
            
        }
    }
    public async Task<(int Code, string Msg, JObject ContentInfo)> UserLogin(string token, string email = "", string password = "", string auth2fa = "", bool saveToken = false)
    {
        if (string.IsNullOrEmpty(token))
        {
            // 发送邮箱和密码，请求登录，获取MSL-User-Token
            try
            {
                JObject body;
                if (string.IsNullOrEmpty(auth2fa))
                {
                    body = new JObject
                    {
                        ["email"] = email,
                        ["password"] = password
                    };
                }
                else
                {
                    body = new JObject
                    {
                        ["email"] = email,
                        ["password"] = password,
                        ["twoFactorAuthKey"] = auth2fa
                    };
                }
                HttpResponse res = await MSLUserService.PostAsync("/user/login", 0, body);
                if (res.IsSuccess)
                {
                    JObject LoginResponse = JObject.Parse((string)res.Content);
                    if (LoginResponse["code"].Value<int>() != 200)
                    {
                        return ((int)LoginResponse["code"], LoginResponse["msg"].ToString(), LoginResponse);
                    }
                    token = LoginResponse["data"]["token"].ToString();
                }
                else
                {
                    return ((int)res.StatusCode, res.Content.ToString(), null);
                }
            }
            catch (Exception ex)
            {
                return (0, ex.Message, null);
            }
        }

        try
        {
            var headersAction = new Action<HttpRequestHeaders>(headers =>
            {
                headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            });
            HttpResponse res = await HttpService.GetAsync("/frp/userInfo", headersAction,uaType:UAManager.UAType.MSLX);
            if (res.IsSuccess)
            {
                var loginRes = JObject.Parse(res.Content.ToString());
                if ((int)loginRes["code"] != 200)
                {
                    return ((int)loginRes["code"], loginRes["msg"].ToString(), null);
                }
                //UserToken = token;

                // 用户登陆成功后，发送POST请求续期Token
                _ = await HttpService.PostAsync("/user/renewToken", HttpService.PostContentType.None, configureHeaders: headersAction);
                return (200, string.Empty, JObject.Parse(res.Content.ToString()));
            }
            return (200, string.Empty, null);
        }
        catch (Exception ex)
        {
            return (0, ex.Message, null);
        }
    }

    private void RegisterButton_Click(object? sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://user.mslmc.net/register") { UseShellExecute = true });
    }

    // 获取信息
    private async Task GetFrpInfoAsync()
    {
        try
        {
            var dialog = DialogService.DialogManager.CreateDialog()
                .WithTitle("登录中")
                .WithContent(new TextBlock { Text = "获取用户信息……" });
            dialog.TryShow();

            var response = await MSLUserService.GetAsync("/frp/userInfo", new Dictionary<string, string> { ["Authorization"] = $"Bearer {_userToken}" });

            DialogService.DialogManager.DismissDialog();

            if (!response.IsSuccess || response.Content == null)
            {
                DialogService.ToastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("获取用户信息失败，请重新登录")
                .WithContent((response.Exception as Exception)?.Message ?? string.Empty)
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Queue();
                ShowLoginPage();
                return;
            }

            JObject json = JObject.Parse(response.Content);
            if (json["code"]?.Value<int>() == 200)
            {
                // 填充用户信息
                UsernameText.Text = json["data"]?["name"]?.Value<string>() ?? string.Empty;
                int userGroup = json["data"]?["user_group"]?.Value<int>() ?? 0;
                UserGroupText.Text = userGroup == 6 ? "超级管理员"
                    : userGroup == 1 ? "高级会员"
                    : userGroup == 2 ? "超级会员" : "普通用户";
                UserMaxTunnelsText.Text = json["data"]?["maxTunnelCount"]?.Value<string>() ?? string.Empty;
                long outdated = json["data"]?["outdated"]?.Value<long>() ?? 0;
                UserOutdatedText.Text = outdated == 3749682420
                    ? "长期有效"
                    : outdated.ToString();

                DialogService.ToastManager.CreateToast()
                .OfType(NotificationType.Success)
                .WithTitle("登录成功！")
                .WithContent("成功登录到MSL Frp服务")
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Queue();
                ShowMainPage();
                await GetNodes();
                await GetTunnels();
            }
            else
            {
                DialogService.ToastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("获取用户信息失败")
                .WithContent(json["msg"]?.ToString() ?? string.Empty)
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Queue();
                ShowLoginPage();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
            DialogService.DialogManager.DismissDialog();
            DialogService.ToastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("获取用户信息失败")
                .WithContent(ex.Message)
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Queue();
            ShowLoginPage();
        }
    }

    private async Task GetNodes()
    {
        try
        {
            var response = await MSLUserService.GetAsync("/frp/nodeList", new Dictionary<string, string> { ["Authorization"] = $"Bearer {_userToken}" });

            if (response.IsSuccess!=true || response.Content == null)
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
            _nodes.Clear();

            foreach (var nodeItem in data)
            {
                int nodeId = nodeItem["id"]?.Value<int>() ?? 0;
                string nodeName = nodeItem["node"]?.Value<string>() ?? string.Empty;
                _nodeMap[nodeId] = nodeName;

                _nodes.Add(new Node
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

            if (_nodes.Count > 0)
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

            _tunnels.Clear();
            foreach (var tunnel in data)
            {
                int nodeId = tunnel["node_id"]?.Value<int>() ?? 0;
                string nodeName = _nodeMap.ContainsKey(nodeId) ? _nodeMap[nodeId] : "未知节点";
                _tunnels.Add(new Tunnel
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

            if (_tunnels.Count > 0)
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
        if (_selectedTunnel == null)
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
                new Dictionary<string, string> { ["id"] = _selectedTunnel.Id.ToString() },
                new Dictionary<string, string> { ["Authorization"] = $"Bearer {_userToken}" }
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
        if (_selectedTunnel == null)
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
    new Dictionary<string, string> { ["id"] = _selectedTunnel.Id.ToString() },
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

    private void TunnelListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (TunnelListBox.SelectedIndex < 0 || TunnelListBox.SelectedIndex >= _tunnels.Count) return;
        _selectedTunnel = _tunnels[TunnelListBox.SelectedIndex];
        // 填充隧道信息区
        TunnelIdText.Text = $"# {_selectedTunnel.Id} {_selectedTunnel.Name}";
        TunnelRemarksText.Text = _selectedTunnel.Remarks;
        TunnelStatusText.Text = _selectedTunnel.Status;
        TunnelLocalPortText.Text = _selectedTunnel.LocalPort.ToString();
        TunnelRemotePortText.Text = _selectedTunnel.RemotePort.ToString();
        TunnelNodeText.Text = _selectedTunnel.Node;
    }

    // 节点列表页按钮事件
    private async void RefreshNodes_Click(object? sender, RoutedEventArgs e)
        => await GetNodes();

    private void NodeListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (NodeListBox.SelectedIndex < 0 || NodeListBox.SelectedIndex >= _nodes.Count) return;
        _selectedNode = _nodes[NodeListBox.SelectedIndex];
        // 填充节点信息区
        NodeNameText.Text = _selectedNode.Name;
        NodeRemarksText.Text = _selectedNode.Remarks;
        // 根据节点能力启用/禁用协议选项
        UdpItem.IsEnabled = _selectedNode.UdpSupport;
        HttpItem.IsEnabled = _selectedNode.HttpSupport;
        HttpsItem.IsEnabled = _selectedNode.HttpSupport;
    }

    private async void CreateTunnel_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedNode == null)
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
                    ["id"] = _selectedNode.Id.ToString(),
                    ["type"] = type,
                    ["remarks"] = $"Create By MSLX {Assembly.GetExecutingAssembly().GetName().Version}",
                    ["use_kcp"] = CreateKcpToggle.IsChecked == true ? "true" : "false"
                },
                new Dictionary<string, string> { ["Authorization"] = $"Bearer {_userToken}" }
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