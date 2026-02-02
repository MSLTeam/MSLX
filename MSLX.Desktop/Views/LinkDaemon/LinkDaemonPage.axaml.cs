using Avalonia.Controls;
using Material.Icons;
using Material.Icons.Avalonia;
using MSLX.Desktop.Models;
using MSLX.Desktop.Utils;
using SukiUI.Controls;
using SukiUI.Dialogs;
using SukiUI.Toasts;
using System;
using System.Linq;

namespace MSLX.Desktop.Views.LinkDaemon;

public partial class LinkDaemonPage : UserControl
{
    public LinkDaemonPage()
    {
        InitializeComponent();

        this.DoneBtn.Click += DoneBtn_Click;
        this.DownloadDaemonBtn.Click += DownloadDaemonBtn_Click;
    }

    private async void DoneBtn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var ip = DaemonAddressTextBox.Text?.Trim().TrimEnd('/') ?? string.Empty;
        var key = DaemonKeyTextBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(ip) || string.IsNullOrEmpty(key))
        {
            DialogService.ToastManager.CreateToast()
                .OfType(Avalonia.Controls.Notifications.NotificationType.Error)
                .WithTitle("错误")
                .WithContent("请填写完整的Daemon地址和API Key。")
                .Dismiss().After(TimeSpan.FromSeconds(5))
                .Queue();
            return;
        }

        ConfigStore.DaemonAddress = ip;
        ConfigStore.DaemonApiKey = key;
        var (isSuccess,_) = await DaemonManager.VerifyDaemonApiKey();
        if (isSuccess)
        {
            if(RememberLinkInfo.IsChecked == true)
            {
                // 记住Daemon地址和API Key
                ConfigService.Config.WriteConfigKey("DaemonAddress", ip);
                ConfigService.Config.WriteConfigKey("DaemonApiKey", key);
            }
            // 验证成功，跳转到主页面
            SideMenuHelper.MainSideMenuHelper?.ShowMainPages();
            SideMenuHelper.MainSideMenuHelper?.NavigateRemove(this);
        }
    }

    private void DownloadDaemonBtn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SideMenuHelper.MainSideMenuHelper?.NavigateRemove<LinkDaemonPage>();
        SideMenuHelper.MainSideMenuHelper?.NavigateTo(new SukiSideMenuItem
        {
            Header = "下载守护程序",
            Icon = new MaterialIcon()
            {
                Kind = MaterialIconKind.Download,
            },
            IsContentMovable = false,
            PageContent = new DownloadDaemonPage()
        }, true);
    }
}