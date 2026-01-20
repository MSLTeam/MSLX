using Avalonia.Controls;
using Material.Icons;
using Material.Icons.Avalonia;
using MSLX.Desktop.Models;
using MSLX.Desktop.Utils;
using MSLX.Desktop.Utils.API;
using SukiUI.Controls;
using SukiUI.Dialogs;
using SukiUI.Toasts;
using System;
using System.Linq;
using System.Threading.Tasks;

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
        var ip = DaemonAddressTextBox.Text?.Trim() ?? string.Empty;
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

        ConfigStore.DaemonLink = ip + "/api";
        ConfigStore.DaemonApiKey = key;
        await VerifyDaemonApiKey();
    }

    private async Task VerifyDaemonApiKey()
    {
        // 关闭先前对话框并显示验证中对话框
        DialogService.DialogManager.DismissDialog();
        DialogService.DialogManager.CreateDialog()
            .WithTitle("验证中...")
            .WithContent("请稍候，正在验证Daemon API Key的有效性。")
            .TryShow();
        var (isSuccess, msg, clientName, version, serverTime) = await DaemonAPIService.VerifyDaemonApiKey();
        DialogService.DialogManager.DismissDialog();
        if (isSuccess)
        {
            // 验证成功，跳转到主页面
            SideMenuHelper.MainSideMenuHelper?.ShowMainPages();
            SideMenuHelper.MainSideMenuHelper?.NavigateRemove(this);

            DialogService.ToastManager.CreateToast()
                        .WithTitle(msg)
                        .WithContent(new TextBlock
                        {
                            Text = $"Client Name: {clientName}\nVersion: {version}\nServer Time: {serverTime}",
                        })
                        .Dismiss().After(TimeSpan.FromSeconds(5))
                        .Queue();
        }
        else
        {
            // 验证失败，提示用户并让其重新输入
            // API Key无效
            ConfigStore.DaemonApiKey = string.Empty;
            DialogService.DialogManager.CreateDialog()
                .OfType(Avalonia.Controls.Notifications.NotificationType.Error)
                .WithTitle("验证失败")
                .WithContent("API Key无效，请重新输入。")
                .WithActionButton("确定", _ => { }, true)
                .TryShow();
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