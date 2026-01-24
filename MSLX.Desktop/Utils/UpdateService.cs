using Avalonia.Controls;
using Avalonia.Threading;
using Material.Icons;
using Material.Icons.Avalonia;
using Microsoft.AspNetCore.SignalR.Client;
using MSLX.Desktop.Models;
using MSLX.Desktop.Utils.API;
using MSLX.Desktop.Views.LinkDaemon;
using Newtonsoft.Json.Linq;
using SukiUI.Controls;
using SukiUI.Dialogs;
using SukiUI.Toasts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace MSLX.Desktop.Utils
{
    internal class UpdateService
    {

        public static async Task<bool> UpdateDesktopApp()
        {
            // 由于MSL API接口尚未完工，此处暂时搁置！
            return true;
        }

        private static DaemonUpdateService? _updateService;
        public static async Task<(bool isSuccess, string? msg)> UpdateDaemonApp()
        {
            var (Success, Data, Msg) = await DaemonAPIService.GetJsonDataAsync("/api/update/info");
            if (!Success)
            {
                return (false, Msg);
            }
            if (Data is not JObject apiData)
            {
                return (false, "未获取到更新信息");
            }
            bool needUpdate = apiData.Value<bool>("needUpdate");
            string? currentVersion = apiData.Value<string>("currentVersion");
            string? latestVersion = apiData.Value<string>("latestVersion");
            string? status = apiData.Value<string>("status");
            string? updateLog = apiData.Value<string>("log");

            if (!needUpdate)
            {
                return (true, "当前已是最新版本");
            }

            switch (status)
            {
                case "outdated":
                    DialogService.DialogManager.CreateDialog()
                        .OfType(Avalonia.Controls.Notifications.NotificationType.Information)
                        .WithTitle("守护程序：新版本")
                        .WithContent($"检测到守护程序需要进行新版本更新！\n当前版本：{currentVersion}\n最新版本：{latestVersion}\n更新日志：\n{updateLog}")
                        .WithActionButton("更新", async _ =>
                        {
                            await StartUpdateProcessAsync(false);
                        }, true)
                        .WithActionButton("关闭", _ => { }, true, "Standard")
                        .TryShow();
                    break;
                case "release":
                    DialogService.ToastManager.CreateToast()
                        .WithTitle("守护程序：正式版")
                        .WithContent("您的守护程序已是最新正式版！")
                        .Dismiss().After(TimeSpan.FromSeconds(3))
                        .Queue();
                    break;
                case "beta":
                    DialogService.ToastManager.CreateToast()
                        .WithTitle("守护程序：Beta版")
                        .WithContent("您正在使用Beta版守护程序！")
                        .Dismiss().After(TimeSpan.FromSeconds(3))
                        .Queue();
                    break;
            }

            DialogService.DialogManager.CreateDialog()
                .WithTitle("检测到新版本")
                .WithContent(Data ?? string.Empty)
                .TryShow();
            return (true, null);
        }


        /// <summary>
        /// 启动更新流程并显示实时进度
        /// </summary>
        private static async Task StartUpdateProcessAsync(bool autoRestart)
        {
            try
            {
                // 创建进度条和状态显示
                var progressBar = new ProgressBar
                {
                    Value = 0,
                    ShowProgressText = true,
                    Minimum = 0,
                    Maximum = 100
                };

                var statusText = new TextBlock
                {
                    Text = "准备更新...",
                    Margin = new Avalonia.Thickness(0, 10, 0, 0)
                };

                var speedText = new TextBlock
                {
                    Text = "速度: 0 KB/s",
                    Margin = new Avalonia.Thickness(0, 5, 0, 0),
                };

                var stackPanel = new StackPanel();
                stackPanel.Children.Add(progressBar);
                stackPanel.Children.Add(statusText);
                stackPanel.Children.Add(speedText);

                var toast = DialogService.ToastManager.CreateToast()
                    .WithTitle("守护程序更新中...")
                    .WithContent(stackPanel)
                    .Queue();

                // 初始化更新服务
                _updateService = new DaemonUpdateService();

                // 订阅更新进度事件
                _updateService.OnUpdateProgress += async (data) =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        progressBar.Value = data.Progress;
                        speedText.Text = $"速度: {data.Speed}";

                        // 根据阶段显示不同的状态文本
                        statusText.Text = data.Stage switch
                        {
                            "downloading" => $"下载中... {data.Progress:F1}%",
                            "extracting" => "解压核心文件中...",
                            _ => data.Status
                        };
                    });
                    if (data.Stage == "restarting")
                    {
                        await _updateService.DisconnectAsync();
                        _updateService.Dispose();
                        await Task.Delay(1500);
                        DialogService.ToastManager.Dismiss(toast);
                        SideMenuHelper.MainSideMenuHelper?.NavigateTo(new SukiSideMenuItem
                        {
                            Header = "欢迎",
                            Icon = new MaterialIcon()
                            {
                                Kind = MaterialIconKind.HandWave,
                            },
                            PageContent = new WelcomePage(),
                            IsContentMovable = false
                        }, true, 0);
                        SideMenuHelper.MainSideMenuHelper?.HideMainPages(0);
                    }
                    else if (!autoRestart && data.Stage == "completed")
                    {
                        await _updateService.DisconnectAsync();
                        _updateService.Dispose();
                        await Task.Delay(1500);
                        DialogService.ToastManager.Dismiss(toast);
                        await DaemonManager.StopRunningDaemon();
                        await Task.Delay(1500);
                        string currentFileName;
                        string newFileName;
                        switch (PlatformHelper.GetOS())
                        {
                            case PlatformHelper.TheOSPlatform.Windows:
                                currentFileName = Path.Combine(ConfigService.GetAppDataPath(), "MSLX-Daemon.exe");
                                newFileName = Path.Combine(ConfigService.GetAppDataPath(), "MSLX-Daemon.exe.new");
                                if (!(File.Exists(currentFileName) && File.Exists(newFileName)))
                                {
                                    DialogService.DialogManager.CreateDialog()
                                        .OfType(Avalonia.Controls.Notifications.NotificationType.Error)
                                        .WithTitle("更新失败")
                                        .WithContent($"文件不存在！")
                                        .WithActionButton("确定", _ => { }, true)
                                        .TryShow();
                                }
                                File.Delete(currentFileName);
                                File.Copy(newFileName, currentFileName);
                                break;
                            default:
                                currentFileName = Path.Combine(ConfigService.GetAppDataPath(), "MSLX-Daemon");
                                newFileName = Path.Combine(ConfigService.GetAppDataPath(), "MSLX-Daemon.new");
                                if (!(File.Exists(currentFileName) && File.Exists(newFileName)))
                                {
                                    DialogService.DialogManager.CreateDialog()
                                        .OfType(Avalonia.Controls.Notifications.NotificationType.Error)
                                        .WithTitle("更新失败")
                                        .WithContent($"文件不存在！")
                                        .WithActionButton("确定", _ => { }, true)
                                        .TryShow();
                                }
                                File.Delete(currentFileName);
                                File.Copy(newFileName, currentFileName);
                                break;
                        }

                        SideMenuHelper.MainSideMenuHelper?.NavigateTo(new SukiSideMenuItem
                        {
                            Header = "欢迎",
                            Icon = new MaterialIcon()
                            {
                                Kind = MaterialIconKind.HandWave,
                            },
                            PageContent = new WelcomePage(),
                            IsContentMovable = false
                        }, true, 0);
                        SideMenuHelper.MainSideMenuHelper?.HideMainPages(0);
                    }
                };

                // 订阅更新失败事件
                _updateService.OnUpdateFailed += (errorMsg) =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        DialogService.ToastManager.Dismiss(toast);
                        DialogService.DialogManager.CreateDialog()
                            .OfType(Avalonia.Controls.Notifications.NotificationType.Error)
                            .WithTitle("更新失败")
                            .WithContent($"更新过程中发生错误：\n{errorMsg}")
                            .WithActionButton("确定", _ => { }, true)
                            .TryShow();

                        _updateService?.Dispose();
                    });
                };

                // 连接到SignalR Hub
                bool connected = await _updateService.ConnectAsync();
                if (!connected)
                {
                    DialogService.ToastManager.Dismiss(toast);
                    DialogService.DialogManager.CreateDialog()
                        .OfType(Avalonia.Controls.Notifications.NotificationType.Error)
                        .WithTitle("连接失败")
                        .WithContent("无法连接到更新服务，请检查守护程序是否正常运行")
                        .WithActionButton("确定", _ => { }, true)
                        .TryShow();
                    return;
                }

                // 发起更新请求
                var response = await DaemonAPIService.PostApiAsync(
                    "/api/update",
                    new Dictionary<string, string>
                    {
                        {"autoRestart",autoRestart.ToString().ToLower() }
                    },
                    HttpService.PostContentType.Json,
                    null
                );

                if (!response.IsSuccess)
                {
                    DialogService.ToastManager.Dismiss(toast);
                    DialogService.DialogManager.CreateDialog()
                        .OfType(Avalonia.Controls.Notifications.NotificationType.Error)
                        .WithTitle("更新请求失败")
                        .WithContent($"无法启动更新：{response.Content}")
                        .WithActionButton("确定", _ => { }, true)
                        .TryShow();

                    await _updateService.DisconnectAsync();
                    _updateService.Dispose();
                    return;
                }

                // 解析响应
                if (!string.IsNullOrEmpty(response.Content))
                {
                    var json = JObject.Parse(response.Content);
                    if (json["code"]?.ToString() != "200")
                    {
                        DialogService.ToastManager.Dismiss(toast);
                        DialogService.DialogManager.CreateDialog()
                            .OfType(Avalonia.Controls.Notifications.NotificationType.Warning)
                            .WithTitle("更新提示")
                            .WithContent(json["message"]?.ToString() ?? "未知错误")
                            .WithActionButton("确定", _ => { }, true)
                            .TryShow();

                        await _updateService.DisconnectAsync();
                        _updateService.Dispose();
                        return;
                    }
                }

            }
            catch (Exception ex)
            {
                DialogService.DialogManager.CreateDialog()
                    .OfType(Avalonia.Controls.Notifications.NotificationType.Error)
                    .WithTitle("更新异常")
                    .WithContent($"更新过程发生异常：\n{ex.Message}")
                    .WithActionButton("确定", _ => { }, true)
                    .TryShow();

                _updateService?.Dispose();
            }
        }

    }

    public class DaemonUpdateService : IDisposable
    {
        private HubConnection? _hubConnection;
        private bool _isConnected = false;

        // 更新进度事件
        public event Action<UpdateProgressData>? OnUpdateProgress;
        public event Action<string>? OnUpdateFailed;

        /// <summary>
        /// 连接到更新进度Hub
        /// </summary>
        public async Task<bool> ConnectAsync()
        {
            try
            {
                if (_hubConnection != null && _isConnected)
                {
                    return true;
                }

                string hubUrl = $"{ConfigStore.DaemonAddress}/api/hubs/daemonUpdate";

                _hubConnection = new HubConnectionBuilder()
                    .WithUrl(hubUrl, options =>
                    {
                        options.Headers.Add("x-api-key", ConfigStore.DaemonApiKey);
                    })
                    .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5) })
                    .Build();

                // 注册更新进度回调
                _hubConnection.On<UpdateProgressData>("UpdateProgress", (data) =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        OnUpdateProgress?.Invoke(data);
                    });
                });

                // 注册更新失败回调
                _hubConnection.On<string>("UpdateFailed", (errorMsg) =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        OnUpdateFailed?.Invoke(errorMsg);
                    });
                });

                // 连接状态处理
                _hubConnection.Reconnecting += error =>
                {
                    _isConnected = false;
                    Console.WriteLine($"更新Hub重连中: {error?.Message}");
                    return Task.CompletedTask;
                };

                _hubConnection.Reconnected += connectionId =>
                {
                    _isConnected = true;
                    Console.WriteLine($"更新Hub已重连: {connectionId}");
                    return Task.CompletedTask;
                };

                _hubConnection.Closed += error =>
                {
                    _isConnected = false;
                    Console.WriteLine($"更新Hub连接关闭: {error?.Message}");
                    return Task.CompletedTask;
                };

                await _hubConnection.StartAsync();
                _isConnected = true;
                Console.WriteLine("更新Hub连接成功");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"连接更新Hub失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public async Task DisconnectAsync()
        {
            if (_hubConnection != null)
            {
                await _hubConnection.StopAsync();
                await _hubConnection.DisposeAsync();
                _hubConnection = null;
                _isConnected = false;
            }
        }

        public void Dispose()
        {
            _ = DisconnectAsync();
        }
    }

    /// <summary>
    /// 更新进度数据模型
    /// </summary>
    public class UpdateProgressData
    {
        public double Progress { get; set; }
        public string Speed { get; set; } = "0 KB/s";
        public string Stage { get; set; } = "";
        public string Status { get; set; } = "";
    }
}
