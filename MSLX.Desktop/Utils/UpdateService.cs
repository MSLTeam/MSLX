using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MSLX.Desktop.Utils
{
    internal class UpdateService
    {

        public static async Task<bool> UpdateDesktopApp()
        {
            var (Success, Data, Msg) = await MSLAPIService.GetJsonDataAsync("/query/update?software=MSLX");
            if (!Success)
            {
                DialogService.ToastManager.CreateToast()
                            .OfType(Avalonia.Controls.Notifications.NotificationType.Error)
                            .WithTitle("更新失败")
                            .WithContent(Msg)
                            .Dismiss().After(TimeSpan.FromSeconds(5))
                            .Queue();
                return false;
            }
            if (Data is not JObject apiData)
            {
                DialogService.ToastManager.CreateToast()
                            .OfType(Avalonia.Controls.Notifications.NotificationType.Error)
                            .WithTitle("更新失败")
                            .WithContent("未获取到更新信息")
                            .Dismiss().After(TimeSpan.FromSeconds(5))
                            .Queue();
                return false;
            }

            var latestVersion = Version.Parse(apiData.Value<string>("desktopLatestVersion") ?? "0.0");
            if (latestVersion <= ConfigStore.Version)
            {
                DialogService.ToastManager.CreateToast()
                            .OfType(Avalonia.Controls.Notifications.NotificationType.Success)
                            .WithTitle("最新版本")
                            .WithContent("当前已是最新版本。")
                            .Dismiss().After(TimeSpan.FromSeconds(5))
                            .Queue();
                return true;
            }

            // 构建下载 URL
            string arch = PlatformHelper.GetOSArch() switch
            {
                PlatformHelper.TheArchitecture.Arm64 => "arm64",
                _ => "x64"
            };
            string system = PlatformHelper.GetOS() switch
            {
                PlatformHelper.TheOSPlatform.Windows => "Windows",
                PlatformHelper.TheOSPlatform.OSX => "macOS",
                _ => "Linux"
            };

            // 弹出确认对话框
            DialogService.DialogManager.CreateDialog()
                .OfType(Avalonia.Controls.Notifications.NotificationType.Information)
                .WithTitle("新版本可用")
                .WithContent($"检测到新版本 {latestVersion} 可用！\n当前版本：{ConfigStore.Version}\n是否立即下载更新？")
                .WithActionButton("下载", async _ =>
                {
                    await StartDesktopUpdateAsync(latestVersion, system, arch);
                }, true)
                .WithActionButton("取消", _ => { }, true, "Standard")
                .TryShow();

            return true;
        }

        private static async Task StartDesktopUpdateAsync(Version latestVersion, string system, string arch)
        {
            try
            {
                var progressPanel = new UpdateProgressPanel();
                var toast = DialogService.ToastManager.CreateToast()
                    .WithTitle("桌面程序更新中...")
                    .WithContent(progressPanel)
                    .Queue();

                var (Success, Data, Msg) = await MSLAPIService.GetJsonDataAsync($"/download/update?software=MSLX-Desktop&system={system}&arch={arch}");
                if (!Success)
                {
                    DialogService.ToastManager.Dismiss(toast);
                    DialogService.ToastManager.CreateToast()
                        .OfType(Avalonia.Controls.Notifications.NotificationType.Error)
                        .WithTitle("更新失败").WithContent(Msg)
                        .Dismiss().After(TimeSpan.FromSeconds(5)).Queue();
                    return;
                }
                if (Data is not JObject apiData)
                {
                    DialogService.ToastManager.Dismiss(toast);
                    return;
                }

                string? downloadUrl = apiData.Value<string>("file");
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    DialogService.ToastManager.Dismiss(toast);
                    return;
                }

                var (taskId, _) = DownloadManager.DownloadWithDefaultGroup(
                    downloadUrl,
                    ConfigService.GetAppDataPath(),
                    null
                );

                progressPanel.BindToTask(taskId);
                await DownloadManager.Instance.WaitForItemCompletionAsync(taskId);

                var task = DownloadManager.Instance.GetDownloadItem(taskId);
                if (task == null || string.IsNullOrEmpty(task.Filename))
                {
                    DialogService.ToastManager.Dismiss(toast);
                    DialogService.DialogManager.CreateDialog()
                        .OfType(Avalonia.Controls.Notifications.NotificationType.Error)
                        .WithTitle("更新失败").WithContent("下载任务信息获取失败！")
                        .WithActionButton("确定", _ => { }, true).TryShow();
                    return;
                }
                if (task.Status == DownloadStatus.Failed)
                {
                    DialogService.ToastManager.Dismiss(toast);
                    DialogService.DialogManager.CreateDialog()
                        .OfType(Avalonia.Controls.Notifications.NotificationType.Error)
                        .WithTitle("更新失败").WithContent($"下载失败：{task.ErrorMessage}")
                        .WithActionButton("确定", _ => { }, true).TryShow();
                    return;
                }

                // 解压
                progressPanel.SetStatus("解压更新包中...");

                string archivePath = Path.Combine(ConfigService.GetAppDataPath(), task.Filename);
                string extractDir = Path.Combine(ConfigService.GetAppDataPath(), $"update_tmp_{latestVersion}");

                if (Directory.Exists(extractDir))
                    Directory.Delete(extractDir, true);
                Directory.CreateDirectory(extractDir);

                await Task.Run(() =>
                {
                    if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        System.IO.Compression.ZipFile.ExtractToDirectory(archivePath, extractDir, true);
                    else
                        ExtractTarGz(archivePath, extractDir);
                });

                // 找到可执行文件
                string exeName = PlatformHelper.GetOS() switch
                {
                    PlatformHelper.TheOSPlatform.Windows => "MSLX-Desktop.exe",
                    _ => "MSLX-Desktop"
                };

                string? newExePath = Directory
                    .EnumerateFiles(extractDir, exeName, SearchOption.AllDirectories)
                    .FirstOrDefault();

                if (string.IsNullOrEmpty(newExePath) || !File.Exists(newExePath))
                {
                    DialogService.ToastManager.Dismiss(toast);
                    DialogService.DialogManager.CreateDialog()
                        .OfType(Avalonia.Controls.Notifications.NotificationType.Error)
                        .WithTitle("更新失败").WithContent($"在解压目录中未找到 {exeName}，请手动更新。")
                        .WithActionButton("确定", _ => { }, true).TryShow();
                    return;
                }

                // 当前程序所在目录(含文件名)
                string currentExePath = Path.Combine(
                    AppContext.BaseDirectory,
                    exeName
                );

                DialogService.ToastManager.Dismiss(toast);

                // 平台分发替换逻辑
                switch (PlatformHelper.GetOS())
                {
                    case PlatformHelper.TheOSPlatform.Windows:
                        await ReplaceOnWindows(newExePath, currentExePath, extractDir, archivePath);
                        break;

                    default:
                        await ReplaceOnUnix(newExePath, currentExePath, extractDir, archivePath);
                        break;
                }
            }
            catch (Exception ex)
            {
                DialogService.DialogManager.CreateDialog()
                    .OfType(Avalonia.Controls.Notifications.NotificationType.Error)
                    .WithTitle("更新异常").WithContent($"更新过程发生异常：\n{ex.Message}")
                    .WithActionButton("确定", _ => { }, true).TryShow();
            }
        }

        // Windows：批处理脚本延迟替换（绕过文件占用）
        private static async Task ReplaceOnWindows(
            string newExePath, string currentExePath,
            string extractDir, string archivePath)
        {
            string batPath = Path.Combine(Path.GetTempPath(), "mslx_update.bat");
            string logPath = Path.Combine(Path.GetTempPath(), "mslx_update.log");

            // 用 :retry 循环等待主进程退出后再替换
            string bat = $"""
        @echo off
        chcp 65001 >nul
        set TARGET="{currentExePath}"
        set SOURCE="{newExePath}"
        set APP="{currentExePath}"

        :retry
        taskkill /f /im MSLX-Desktop.exe >nul 2>&1
        timeout /t 1 /nobreak >nul
        copy /y %SOURCE% %TARGET% >"{logPath}" 2>&1
        if errorlevel 1 goto retry

        rd /s /q "{extractDir}"
        del /f /q "{archivePath}"
        start "" %APP%
        del "%~f0"
        """;

            await File.WriteAllTextAsync(batPath, bat, System.Text.Encoding.UTF8);

            DialogService.ToastManager.CreateToast()
                .OfType(Avalonia.Controls.Notifications.NotificationType.Information)
                .WithTitle("即将重启更新")
                .WithContent("新版本下载完成，程序将关闭并自动替换，完成后自动重启。")
                .Queue();

            await Task.Delay(1000);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{batPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });

            var app = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            app?.Shutdown();
            // Environment.Exit(0);
        }

        // Linux / macOS：直接替换后重启
        private static async Task ReplaceOnUnix(string newExePath, string currentExePath,
            string extractDir, string archivePath)
        {
            DialogService.DialogManager.CreateDialog()
                .OfType(Avalonia.Controls.Notifications.NotificationType.Information)
                .WithTitle("即将重启更新")
                .WithContent($"新版本下载完成，程序将关闭并自动替换，完成后自动重启。")
                .WithActionButton("立即更新", async _ =>
                {
                    // 赋予可执行权限
                    await Task.Run(() =>
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "chmod",
                            Arguments = $"+x \"{newExePath}\"",
                            UseShellExecute = false
                        })?.WaitForExit();
                    });

                    // 写 shell 脚本：等待主进程退出 → 替换 → 重启
                    string shPath = Path.Combine(Path.GetTempPath(), "mslx_update.sh");
                    int pid = Environment.ProcessId;

                    string sh = $"""
                #!/bin/sh
                while kill -0 {pid} 2>/dev/null; do sleep 0.5; done
                cp -f "{newExePath}" "{currentExePath}"
                chmod +x "{currentExePath}"
                rm -rf "{extractDir}"
                rm -f  "{archivePath}"
                "{currentExePath}" &
                rm -- "$0"
                """;

                    await File.WriteAllTextAsync(shPath, sh, System.Text.Encoding.UTF8);

                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "chmod",
                        Arguments = $"+x \"{shPath}\"",
                        UseShellExecute = false
                    })?.WaitForExit();

                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "/bin/sh",
                        Arguments = $"\"{shPath}\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });

                    Environment.Exit(0);
                }, true)
                .WithActionButton("取消", _ =>
                {
                    try { Directory.Delete(extractDir, true); } catch { }
                    try { File.Delete(archivePath); } catch { }
                }, true, "Standard")
                .TryShow();
        }

        // tar.gz 解压（.NET 无内置支持，用 SharpCompress 或 tar 命令）
        private static void ExtractTarGz(string archivePath, string destDir)
        {
            // 方案 A：调用系统 tar（Linux/macOS 均有，Windows 10 1803+ 也有）
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "tar",
                Arguments = $"-xzf \"{archivePath}\" -C \"{destDir}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };
            var proc = System.Diagnostics.Process.Start(psi)
                       ?? throw new Exception("无法启动 tar 进程");
            string err = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            if (proc.ExitCode != 0)
                throw new Exception($"tar 解压失败：{err}");
        }

        public static async Task<(bool isSuccess, string? msg)> UpdateDaemonApp(bool autoRestart)
        {
            var (Success, Data, Msg) = await DaemonAPIService.GetJsonDataAsync("/api/update/info");
            if (!Success)
            {
                DialogService.ToastManager.CreateToast()
                            .OfType(Avalonia.Controls.Notifications.NotificationType.Error)
                            .WithTitle("更新失败")
                            .WithContent(new TextBlock
                            {
                                Text = Msg,
                            })
                            .Dismiss().After(TimeSpan.FromSeconds(5))
                            .Queue();
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
                            await StartUpdateProcessAsync(autoRestart);
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
            var progressPanel = new UpdateProgressPanel();
            var toast = DialogService.ToastManager.CreateToast()
                .WithTitle("守护程序更新中...")
                .WithContent(progressPanel)
                .Queue();

            // 初始化更新服务
            var _updateService = new DaemonUpdateService();

            try
            {
                // 订阅更新进度事件
                _updateService.OnUpdateProgress += async (data) =>
                {
                    progressPanel.UpdateProgress(data);
                    if (data.Stage == "restarting")
                    {
                        await _updateService.DisconnectAsync();
                        _updateService.Dispose();
                        await Task.Delay(500);
                        DialogService.ToastManager.Dismiss(toast);
                        DialogService.DialogManager.CreateDialog()
                            .WithTitle("更新中")
                            .WithContent($"请等待……")
                            .TryShow();
                        await Task.Delay(10000);
                        DialogService.DialogManager.DismissDialog();
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
                        await Task.Delay(500);
                        DialogService.ToastManager.Dismiss(toast);
                        await DaemonManager.StopRunningDaemon();
                        DialogService.DialogManager.CreateDialog()
                            .WithTitle("更新中")
                            .WithContent($"请等待……")
                            .TryShow();
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
                                    DialogService.DialogManager.DismissDialog();
                                    DialogService.DialogManager.CreateDialog()
                                        .OfType(Avalonia.Controls.Notifications.NotificationType.Error)
                                        .WithTitle("更新失败")
                                        .WithContent($"文件不存在！")
                                        .WithActionButton("确定", _ => { }, true)
                                        .TryShow();
                                }
                                File.Delete(currentFileName);
                                await Task.Delay(200);
                                File.Copy(newFileName, currentFileName);
                                await Task.Delay(300);
                                File.Delete(newFileName);
                                break;
                            default:
                                currentFileName = Path.Combine(ConfigService.GetAppDataPath(), "MSLX-Daemon");
                                newFileName = Path.Combine(ConfigService.GetAppDataPath(), "MSLX-Daemon.new");
                                if (!(File.Exists(currentFileName) && File.Exists(newFileName)))
                                {
                                    DialogService.DialogManager.DismissDialog();
                                    DialogService.DialogManager.CreateDialog()
                                        .OfType(Avalonia.Controls.Notifications.NotificationType.Error)
                                        .WithTitle("更新失败")
                                        .WithContent($"文件不存在！")
                                        .WithActionButton("确定", _ => { }, true)
                                        .TryShow();
                                }
                                File.Delete(currentFileName);
                                await Task.Delay(200);
                                File.Copy(newFileName, currentFileName);
                                await Task.Delay(300);
                                File.Delete(newFileName);
                                break;
                        }
                        DialogService.DialogManager.DismissDialog();
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
                try
                {
                    await _hubConnection.StopAsync();
                    await _hubConnection.DisposeAsync();
                }
                finally
                {
                    _hubConnection = null;
                    _isConnected = false;
                }
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

    /// <summary>
    /// 通用更新进度面板，支持两种模式：
    ///   1. BindToTask(taskId): 绑定 DownloadManager 任务（Desktop 更新）
    ///   2. UpdateProgress(data): 手动推送数据（Daemon SignalR 更新）
    /// </summary>
    public class UpdateProgressPanel : UserControl
    {
        private readonly ProgressBar _progressBar;
        private readonly TextBlock _statusText;
        private readonly TextBlock _speedText;

        private CancellationTokenSource? _pollCts;

        public UpdateProgressPanel()
        {
            _progressBar = new ProgressBar
            {
                Value = 0,
                ShowProgressText = true,
                Minimum = 0,
                Maximum = 100
            };

            _statusText = new TextBlock
            {
                Text = "准备中...",
                Margin = new Avalonia.Thickness(0, 10, 0, 0)
            };

            _speedText = new TextBlock
            {
                Text = "速度: 0 KB/s",
                Margin = new Avalonia.Thickness(0, 5, 0, 0)
            };

            var panel = new StackPanel();
            panel.Children.Add(_progressBar);
            panel.Children.Add(_statusText);
            panel.Children.Add(_speedText);

            Content = panel;
        }

        // 绑定 DownloadManager 任务
        /// <summary>
        /// 轮询 DownloadManager 中指定任务的进度并同步到 UI。
        /// </summary>
        public void BindToTask(string taskId, int pollIntervalMs = 200)
        {
            _pollCts?.Cancel();
            _pollCts = new CancellationTokenSource();
            var ct = _pollCts.Token;

            _ = Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    var item = DownloadManager.Instance.GetDownloadItem(taskId);
                    if (item == null) break;

                    Dispatcher.UIThread.Post(() =>
                    {
                        _progressBar.Value = item.Progress.ProgressPercentage;
                        _speedText.Text = $"速度: {item.Progress.BytesPerSecond:N0} B/s";
                        _statusText.Text = item.Status switch
                        {
                            DownloadStatus.InProgress =>
                                $"下载中... {item.Progress.ProgressPercentage:F1}%  " +
                                $"剩余：{item.Progress.EstimatedTimeRemaining:hh\\:mm\\:ss}",
                            DownloadStatus.Completed => "下载完成！",
                            DownloadStatus.Failed => $"下载失败：{item.ErrorMessage}",
                            DownloadStatus.Paused => "已暂停",
                            _ => item.Status.ToString()
                        };
                    });

                    if (item.Status is DownloadStatus.Completed or DownloadStatus.Failed)
                        break;

                    await Task.Delay(pollIntervalMs, ct).ConfigureAwait(false);
                }
            }, ct);
        }

        //Daemon SignalR 手动推送
        /// <summary>
        /// 由外部（SignalR 回调）直接推送进度数据。
        /// </summary>
        public void UpdateProgress(UpdateProgressData data)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _progressBar.Value = data.Progress;
                _speedText.Text = $"速度: {data.Speed}";
                _statusText.Text = data.Stage switch
                {
                    "downloading" => $"下载中... {data.Progress:F1}%",
                    "extracting" => "解压核心文件中...",
                    _ => data.Status
                };
            });
        }

        public void SetStatus(string status)
        {
            Dispatcher.UIThread.Post(() => _statusText.Text = status);
        }

        // 停止轮询（页面卸载时调用）
        public void StopPolling() => _pollCts?.Cancel();
    }
}
