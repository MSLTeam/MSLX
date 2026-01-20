using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
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
using System.IO;
using System.Threading.Tasks;

namespace MSLX.Desktop.Views.LinkDaemon;

public partial class DownloadDaemonPage : UserControl
{
    public DownloadDaemonPage()
    {
        InitializeComponent();

        this.Loaded += DownloadDaemonPage_Loaded;
        this.DoneBtn.Click += DoneBtn_Click;
        this.LinkDaemonBtn.Click += LinkDaemonBtn_Click;
    }

    private void DownloadDaemonPage_Loaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // 根据系统类型和系统架构自动选择ComboBox的选择项
        switch (PlatformHelper.GetOS())
        {
            case PlatformHelper.TheOSPlatform.Windows:
                SysType.SelectedIndex = 0;
                break;
            case PlatformHelper.TheOSPlatform.Linux:
                SysType.SelectedIndex = 1;
                break;
            case PlatformHelper.TheOSPlatform.OSX:
                SysType.SelectedIndex = 2;
                break;
        }
        switch (PlatformHelper.GetOSArch())
        {
            case PlatformHelper.TheArchitecture.X64:
                ArchType.SelectedIndex = 0;
                break;
            case PlatformHelper.TheArchitecture.Arm64:
                ArchType.SelectedIndex = 1;
                break;
        }
    }

    private async void DoneBtn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        string systype = "Windows";
        string archtype = "x64";
        switch (SysType.SelectedIndex)
        {
            case 0:
                systype = "Windows";
                break;
            case 1:
                systype = "Linux";
                break;
            case 2:
                systype = "macOS";
                break;
        }

        switch (ArchType.SelectedIndex)
        {
            case 0:
                archtype = "x64";
                break;
            case 1:
                archtype = "arm64";
                break;
        }

        DoneBtn.IsEnabled = false;

        var (Success,Data,Msg) = await MSLAPIService.GetJsonDataAsync("/download/update", queryParameters: new System.Collections.Generic.Dictionary<string, string>
        {
            {
                "software","MSLX"
            },
            {
                "arch",archtype
            },
            {
                "system",systype
            }
        });

        if (Success)
        {
            string downloadUrl = (Data as JObject)?["file"]?.Value<string>() ?? string.Empty;
            if(string.IsNullOrEmpty(downloadUrl))
            {
                DoneBtn.IsEnabled = true;
                DialogService.ToastManager.CreateToast()
                .OfType(Avalonia.Controls.Notifications.NotificationType.Error)
                .WithTitle("错误")
                .WithContent("下载链接为空！")
                .Dismiss().After(TimeSpan.FromSeconds(5))
                .Queue();
                return;
            }

            var (taskid, task) = DownloadManager.DownloadWithDefaultGroup(downloadUrl,
            ConfigService.GetAppDataPath(),
            null);
            this.DwnDisplay.IsVisible = true;
            this.DwnDisplay.AddTaskToUIDisplay(taskid);

            // 等待下载完成
            await DownloadManager.Instance.WaitForItemCompletionAsync(taskid);

            task = DownloadManager.Instance.GetDownloadItem(taskid);

            if (task == null || string.IsNullOrEmpty(task.Filename))
            {
                DoneBtn.IsEnabled = true;
                DialogService.ToastManager.CreateToast()
                    .OfType(Avalonia.Controls.Notifications.NotificationType.Error)
                    .WithTitle("错误")
                    .WithContent("下载任务信息获取失败！")
                    .Dismiss().After(TimeSpan.FromSeconds(5))
                    .Queue();
                return;
            }

            // 显示下载完成提示
            DialogService.DialogManager.CreateDialog()
                .OfType(Avalonia.Controls.Notifications.NotificationType.Success)
                .WithTitle("下载完成")
                .WithContent($"正在解压: {task.Filename}")
                .TryShow();

            // 解压并启动Daemon
            string downloadedFilePath = Path.Combine(ConfigService.GetAppDataPath(), task.Filename);
            if (await UnzipAndStartDaemon(downloadedFilePath))
            {
                DialogService.ToastManager.CreateToast()
                        .OfType(Avalonia.Controls.Notifications.NotificationType.Information)
                        .WithTitle("提示")
                        .WithContent("解压和启动完成，开始尝试链接至Daemon程序！")
                        .Dismiss().After(TimeSpan.FromSeconds(5))
                        .Queue();
                await Task.Delay(5000);

                await GetKeyAndLinkDaemon();
            }
        }
        else
        {
            DialogService.ToastManager.CreateToast()
                .OfType(Avalonia.Controls.Notifications.NotificationType.Error)
                .WithTitle("错误")
                .WithContent(Msg)
                .Dismiss().After(TimeSpan.FromSeconds(5))
                .Queue();
        }
        DoneBtn.IsEnabled = true;
    }

    /// <summary>
    /// 解压并启动Daemon程序
    /// </summary>
    /// <param name="zipFilePath">下载的压缩包路径</param>
    private async Task<bool> UnzipAndStartDaemon(string zipFilePath)
    {
        try
        {
            // 显示处理中提示
            DialogService.DialogManager.CreateDialog()
                .OfType(Avalonia.Controls.Notifications.NotificationType.Information)
                .WithTitle("处理中")
                .WithContent("正在解压和启动Daemon程序...")
                .TryShow();

            // 调用DaemonManager进行解压和启动
            var (success, message) = await DaemonManager.UnzipAndStartDaemon(zipFilePath);
            DialogService.DialogManager.DismissDialog();
            if (success)
            {
                // 成功启动
                DialogService.ToastManager.CreateToast()
                    .OfType(Avalonia.Controls.Notifications.NotificationType.Success)
                    .WithTitle("成功")
                    .WithContent("Daemon程序已成功安装并启动！")
                    .Dismiss().After(TimeSpan.FromSeconds(3))
                    .Queue();

                return true;
            }
            else
            {
                // 启动失败
                DialogService.DialogManager.CreateDialog()
                    .WithTitle("错误")
                    .WithContent($"Daemon启动失败: {message}")
                    .WithActionButton("确定", _ => { }, true)
                    .TryShow();

                return false;
            }
        }
        catch (Exception ex)
        {
            // 异常处理
            DialogService.DialogManager.CreateDialog()
                .WithTitle("错误")
                .WithContent($"处理Daemon程序时发生错误: {ex.Message}")
                .WithActionButton("确定", _ => { }, true)
                .TryShow();

            return false;
        }
    }

    private async Task GetKeyAndLinkDaemon()
    {
        // 加载配置文件，尝试自动获取Daemon API Key
        ConfigService.GetDaemonApiKey();
        await Task.Delay(500);
        if (!string.IsNullOrEmpty(ConfigStore.DaemonApiKey))
        {
            // 已经有ApiKey，直接验证
            bool isSuccess = await DaemonManager.VerifyDaemonApiKey();
            if (isSuccess)
            {
                // 验证成功，跳转到主页面
                SideMenuHelper.MainSideMenuHelper?.ShowMainPages();
                SideMenuHelper.MainSideMenuHelper?.NavigateRemove(this);
                SideMenuHelper.MainSideMenuHelper?.NavigateTo<HomePage>();
                return;
            }
        }

        DialogService.DialogManager.DismissDialog();
        // 未获取到密钥或验证失败
        DialogService.DialogManager.CreateDialog()
            .OfType(Avalonia.Controls.Notifications.NotificationType.Error)
            .WithTitle("验证失败！")
            .WithContent("未获取到API Key，或验证失败！\n请重试！")
            .WithActionButton("重试", async _ => { await GetKeyAndLinkDaemon(); }, true)
            .WithActionButton("取消", _ => { }, true)
            .TryShow();
    }

    private void LinkDaemonBtn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs? e)
    {
        SideMenuHelper.MainSideMenuHelper?.NavigateRemove<DownloadDaemonPage>();
        SideMenuHelper.MainSideMenuHelper?.NavigateTo(new SukiSideMenuItem
        {
            Header = "链接守护程序",
            Icon = new MaterialIcon()
            {
                Kind = MaterialIconKind.LinkVariant,
            },
            IsContentMovable = false,
            PageContent = new LinkDaemonPage()
        }, true);
    }
}