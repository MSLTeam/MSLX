using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MSLX.Desktop.Services;
using MSLX.Desktop.Utils;
using MSLX.Desktop.Utils.API;
using MSLX.Desktop.ViewModels;
using Newtonsoft.Json.Linq;
using SukiUI.Dialogs;
using SukiUI.Toasts;
using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace MSLX.Desktop.Views;

public partial class PlayerConnectionPage : UserControl
{
    private PlayerConnectionPageViewModel _vm = null!;
    private readonly FrpTunnelSignalRService _signalR = new();
    private const string P2PTunnelPrefix = "P2P_";

    private string? RemotedAddr = null;
    private short RemotedPort;

    public PlayerConnectionPage()
    {
        InitializeComponent();
    }

    #region 生命周期

    private async void Page_Loaded(object? sender, RoutedEventArgs e)
    {
        _vm = (PlayerConnectionPageViewModel)DataContext!;

        if (!_signalR.IsConnected)
        {
            _signalR.LogReceived += OnLogReceived;
            await _signalR.ConnectAsync();
        }

        await LoadP2PServerStatus();
        LoadSavedConfig();
        await LoadExistingTunnel();
    }

    #endregion

    #region 本地配置持久化

    private const string P2PConfigKey = "P2PConfig";

    private void LoadSavedConfig()
    {
        try
        {
            var saved = ConfigService.Config.ReadConfigKey(P2PConfigKey);
            if (saved is not JObject obj) return;

            string role = obj["role"]?.ToString() ?? "";
            string roomName = obj["roomName"]?.ToString() ?? "";
            string secretKey = obj["secretKey"]?.ToString() ?? "";
            int port = obj["port"]?.Value<int>() ?? 25565;

            if (role == "master")
            {
                hostRoomName.Text = roomName;
                hostSecretKey.Text = secretKey;
                hostLocalPort.Value = port;
                hostExpander.IsExpanded = true;
            }
            else if (role == "visitor")
            {
                visitorRoomName.Text = roomName;
                visitorSecretKey.Text = secretKey;
                visitorBindPort.Value = port;
                visitorExpander.IsExpanded = true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"加载P2P本地配置失败: {ex.Message}");
        }
    }

    private void SaveLocalConfig(string role, string roomName, string secretKey, int port)
    {
        var config = new JObject
        {
            ["role"] = role,
            ["roomName"] = roomName,
            ["secretKey"] = secretKey,
            ["port"] = port,
            ["tunnelId"] = _vm.CurrentTunnelId,
            ["tunnelName"] = _vm.CurrentTunnelName
        };
        ConfigService.Config.WriteConfigKey(P2PConfigKey, config);
    }

    private JObject? ReadSavedP2PConfig()
    {
        return ConfigService.Config.ReadConfigKey(P2PConfigKey) as JObject;
    }

    #endregion

    #region 加载状态

    private async Task LoadP2PServerStatus()
    {
        try
        {
            var (success, data, _) = await MSLAPIService.GetJsonDataAsync("software/p2p_server");
            if (success && data is JObject p2pData)
            {
                RemotedAddr = p2pData["ip"]?.ToString() ?? "未知";
                RemotedPort = short.Parse(p2pData["port"]?.ToString() ?? "未知");

                bool reachable = await CheckTcpConnectivity(RemotedAddr, RemotedPort, 3000);
                _vm.StatusText = reachable ? $"在线 ({RemotedAddr}:{RemotedPort})" : $"离线 ({RemotedAddr}:{RemotedPort})";
                _vm.StatusColor = reachable ? "#52c41a" : "#ff4d4f";
            }
            else
            {
                _vm.StatusText = "获取失败";
                _vm.StatusColor = "#ff4d4f";
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"获取P2P服务器状态失败: {ex.Message}");
            _vm.StatusText = "获取失败";
            _vm.StatusColor = "#ff4d4f";
        }
    }

    private async Task LoadExistingTunnel()
    {
        try
        {
            var (success, data, _) = await DaemonAPIService.GetJsonDataAsync("/api/frp/list");
            if (!success || data is not JArray tunnels) return;

            // 优先从本地配置获取隧道信息
            var savedConfig = ReadSavedP2PConfig();
            int savedTunnelId = savedConfig?["tunnelId"]?.Value<int>() ?? -1;
            string? savedTunnelName = savedConfig?["tunnelName"]?.ToString();
            JObject? p2pTunnel = null;

            // 通过保存的ID精确匹配
            if (savedTunnelId != -1)
            {
                p2pTunnel = tunnels
                    .OfType<JObject>()
                    .FirstOrDefault(t => t["id"]?.Value<int>() == savedTunnelId);
            }

            if (p2pTunnel != null)
            {
                bool isRunning = p2pTunnel["status"]!.Value<bool>();
                SetTunnelRunningState(isRunning);
                _vm.CurrentTunnelId = p2pTunnel["id"]!.Value<int>();
                _vm.CurrentTunnelName = p2pTunnel["name"]!.ToString();
                if (isRunning)
                {
                    await _signalR.JoinGroupAsync(_vm.CurrentTunnelId);
                }

                // 同步更新本地配置中的隧道信息
                if (savedConfig != null)
                {
                    SaveLocalConfig(
                        savedConfig["role"]?.ToString() ?? "",
                        savedConfig["roomName"]?.ToString() ?? "",
                        savedConfig["secretKey"]?.ToString() ?? "",
                        savedConfig["port"]?.Value<int>() ?? 25565);
                }
            }
            else
            {
                SetTunnelRunningState(false);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"加载P2P隧道状态失败: {ex.Message}");
        }
    }

    #endregion

    #region 启动 / 停止隧道

    private async void HostStartBtn_Click(object? sender, RoutedEventArgs e)
    {
        await StartTunnelAsync(isMaster: true);
    }

    private async void HostStopBtn_Click(object? sender, RoutedEventArgs e)
    {
        await StopTunnelAsync();
    }

    private async void VisitorStartBtn_Click(object? sender, RoutedEventArgs e)
    {
        await StartTunnelAsync(isMaster: false);
    }

    private async void VisitorStopBtn_Click(object? sender, RoutedEventArgs e)
    {
        await StopTunnelAsync();
    }

    private async Task StartTunnelAsync(bool isMaster)
    {
        if (_vm.IsTunnelRunning)
        {
            ShowToast(NotificationType.Warning, "提示", "已有P2P隧道正在运行，请先停止当前隧道。");
            return;
        }

        string roomName, secretKey;
        int port;

        if (isMaster)
        {
            roomName = hostRoomName.Text?.Trim() ?? "";
            secretKey = hostSecretKey.Text?.Trim() ?? "";
            port = (int)(hostLocalPort.Value ?? 25565);
        }
        else
        {
            roomName = visitorRoomName.Text?.Trim() ?? "";
            secretKey = visitorSecretKey.Text?.Trim() ?? "";
            port = (int)(visitorBindPort.Value ?? 25565);
        }

        if (string.IsNullOrEmpty(roomName) || string.IsNullOrEmpty(secretKey))
        {
            ShowToast(NotificationType.Warning, "提示", "请填写房间名称和连接密码！");
            return;
        }

        // 检查本地保存的配置是否与当前表单一致，决定复用还是重建
        var savedConfig = ReadSavedP2PConfig();
        string role = isMaster ? "master" : "visitor";
        bool configMatch = savedConfig != null
            && savedConfig["role"]?.ToString() == role
            && savedConfig["roomName"]?.ToString() == roomName
            && savedConfig["secretKey"]?.ToString() == secretKey
            && (savedConfig["port"]?.Value<int>() ?? 0) == port;

        int existingTunnelId = savedConfig?["tunnelId"]?.Value<int>() ?? -1;
        string? existingTunnelName = savedConfig?["tunnelName"]?.ToString();

        try
        {
            // 检查 daemon 中是否确实存在该隧道配置
            bool daemonHasTunnel = false;
            if (configMatch && existingTunnelId != -1)
            {
                var (listOk, listData, _) = await DaemonAPIService.GetJsonDataAsync("/api/frp/list");
                if (listOk && listData is JArray tunnelList)
                {
                    daemonHasTunnel = tunnelList.OfType<JObject>()
                        .Any(t => t["id"]?.Value<int>() == existingTunnelId);
                }
            }

            if (configMatch && existingTunnelId != -1 && daemonHasTunnel)
            {
                // 配置未变，直接复用已有隧道，只需启动
                var startResp = await DaemonAPIService.PostApiAsync("/api/frp/action", null,
                    HttpService.PostContentType.Json, new { id = existingTunnelId, action = "start" });

                if (startResp.IsSuccess)
                {
                    _vm.CurrentTunnelId = existingTunnelId;
                    _vm.CurrentTunnelName = existingTunnelName ?? "";
                    SetTunnelRunningState(true);
                    await _signalR.JoinGroupAsync(existingTunnelId);

                    string mode = isMaster ? "房主" : "参与者";
                    ShowToast(NotificationType.Success, "启动成功", $"P2P隧道已启动（{mode}模式，复用已有配置）。");
                    return;
                }
                else
                {
                    // 启动失败，可能是隧道已被删除，继续走重建流程
                    Debug.WriteLine("复用隧道启动失败，将重新创建。");
                }
            }

            // 配置已变或复用失败 → 清理旧隧道并重建
            if (!string.IsNullOrEmpty(_vm.CurrentTunnelName) && _vm.CurrentTunnelId != -1)
            {
                await DaemonAPIService.PostApiAsync("/api/frp/delete", null,
                    HttpService.PostContentType.Json, new { id = _vm.CurrentTunnelId });
            }
            // 也清理本地保存的旧隧道（如果和当前不同的话）
            if (existingTunnelId != -1 && existingTunnelId != _vm.CurrentTunnelId)
            {
                await DaemonAPIService.PostApiAsync("/api/frp/delete", null,
                    HttpService.PostContentType.Json, new { id = existingTunnelId });
            }

            string tomlConfig = GenerateTomlConfig(isMaster, roomName, secretKey, port);
            string tunnelName = $"{P2PTunnelPrefix}{Guid.NewGuid():N}";

            // 创建隧道配置
            var addResp = await DaemonAPIService.PostApiAsync("/api/frp/add", null,
                HttpService.PostContentType.Json,
                new { name = tunnelName, provider = "MSL P2P", config = tomlConfig, format = "toml" });

            if (!addResp.IsSuccess)
            {
                ShowToast(NotificationType.Error, "创建失败", "无法创建P2P隧道配置。");
                return;
            }

            // 获取新创建的隧道ID
            int tunnelId = await FindTunnelIdByNameAsync(tunnelName);
            if (tunnelId == -1)
            {
                ShowToast(NotificationType.Error, "错误", "无法找到刚创建的隧道。");
                return;
            }

            // 启动隧道
            var startResp2 = await DaemonAPIService.PostApiAsync("/api/frp/action", null,
                HttpService.PostContentType.Json, new { id = tunnelId, action = "start" });

            if (startResp2.IsSuccess)
            {
                _vm.CurrentTunnelId = tunnelId;
                _vm.CurrentTunnelName = tunnelName;
                SetTunnelRunningState(true);
                await _signalR.JoinGroupAsync(tunnelId);

                // 保存到本地配置
                SaveLocalConfig(role, roomName, secretKey, port);

                string mode = isMaster ? "房主" : "参与者";
                ShowToast(NotificationType.Success, "启动成功", $"P2P隧道已启动（{mode}模式）。");
            }
            else
            {
                ShowToast(NotificationType.Error, "启动失败", "启动P2P隧道时发生错误。");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"启动P2P隧道失败: {ex.Message}");
            ShowToast(NotificationType.Error, "错误", $"启动P2P隧道时发生错误: {ex.Message}");
        }
    }

    private string GenerateTomlConfig(bool isMaster, string roomName, string secretKey, int port)
    {
        if (isMaster)
        {
            return $"""
                serverAddr = "{RemotedAddr}"
                serverPort = {RemotedPort}

                [[proxies]]
                name = "{roomName}"
                type = "xtcp"
                secretKey = "{secretKey}"
                localIP = "127.0.0.1"
                localPort = {port}
                """;
        }
        else
        {
            return $"""
                serverAddr = "{RemotedAddr}"
                serverPort = {RemotedPort}

                [[visitors]]
                name = "p2p_visitor"
                type = "xtcp"
                serverName = "{roomName}"
                secretKey = "{secretKey}"
                bindAddr = "127.0.0.1"
                bindPort = {port}
                """;
        }
    }

    private async Task StopTunnelAsync()
    {
        if (!_vm.IsTunnelRunning || _vm.CurrentTunnelId == -1) return;

        try
        {
            var resp = await DaemonAPIService.PostApiAsync("/api/frp/action", null,
                HttpService.PostContentType.Json, new { id = _vm.CurrentTunnelId, action = "stop" });

            if (resp.IsSuccess)
            {
                await _signalR.LeaveGroupAsync(_vm.CurrentTunnelId);
                SetTunnelRunningState(false);
                ShowToast(NotificationType.Success, "已停止", "P2P隧道已停止。");
            }
            else
            {
                ShowToast(NotificationType.Error, "停止失败", "停止P2P隧道时发生错误。");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"停止P2P隧道失败: {ex.Message}");
            ShowToast(NotificationType.Error, "错误", $"停止P2P隧道时发生错误: {ex.Message}");
        }
    }

    #endregion

    #region 辅助方法
    private async Task<int> FindTunnelIdByNameAsync(string tunnelName)
    {
        try
        {
            var (success, data, _) = await DaemonAPIService.GetJsonDataAsync("/api/frp/list");
            if (success && data is JArray tunnels)
            {
                var match = tunnels.OfType<JObject>().FirstOrDefault(t => t["name"]?.ToString() == tunnelName);
                if (match != null)
                    return match["id"]!.Value<int>();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"查找隧道ID失败: {ex.Message}");
        }
        return -1;
    }

    private void SetTunnelRunningState(bool isRunning)
    {
        _vm.IsTunnelRunning = isRunning;
        hostStartBtn.IsVisible = !isRunning;
        hostStopBtn.IsVisible = isRunning;
        visitorStartBtn.IsVisible = !isRunning;
        visitorStopBtn.IsVisible = isRunning;

        if (!isRunning)
        {
            logTextBox.Text = string.Empty;
            _vm.Logs = string.Empty;
            _vm.CurrentTunnelId = -1;
            _vm.CurrentTunnelName = string.Empty;
        }
    }

    private static async Task<bool> CheckTcpConnectivity(string host, int port, int timeoutMs)
    {
        if (port <= 0) return false;
        try
        {
            using var client = new TcpClient();
            using var cts = new System.Threading.CancellationTokenSource(timeoutMs);
            await client.ConnectAsync(host, port, cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region Expander 互斥

    private void HostExpander_Expanded(object? sender, RoutedEventArgs e)
    {
        if (visitorExpander != null)
            visitorExpander.IsExpanded = false;
    }

    private void HostExpander_Collapsed(object? sender, RoutedEventArgs e) { }

    private void VisitorExpander_Expanded(object? sender, RoutedEventArgs e)
    {
        if (hostExpander != null)
            hostExpander.IsExpanded = false;
    }

    private void VisitorExpander_Collapsed(object? sender, RoutedEventArgs e) { }

    #endregion

    #region SignalR 日志

    private void OnLogReceived(string log)
    {
        Dispatcher.UIThread.Post(() =>
        {
            logTextBox.Text += log + Environment.NewLine;
            _vm.Logs = logTextBox.Text;
            logTextBox.CaretIndex = logTextBox.Text.Length;
        });
    }

    #endregion

    #region 工具栏

    private async void RefreshBtn_Click(object? sender, RoutedEventArgs e)
    {
        await LoadP2PServerStatus();
        await LoadExistingTunnel();
        ShowToast(NotificationType.Success, "刷新成功", "P2P状态已刷新。");
    }

    #endregion

    #region Toast 快捷方法

    private void ShowToast(NotificationType type, string title, string content)
    {
        DialogService.ToastManager.CreateToast()
            .OfType(type)
            .WithTitle(title)
            .WithContent(content)
            .Dismiss().After(TimeSpan.FromSeconds(5))
            .WithActionButton("关闭", _ => { }, true)
            .Queue();
    }

    #endregion
}
