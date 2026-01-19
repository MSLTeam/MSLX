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
using System.Threading.Tasks;

namespace MSLX.Desktop.Views.LinkDaemon;

public partial class WelcomePage : UserControl
{
    public WelcomePage()
    {
        InitializeComponent();

        this.Loaded += WelcomePage_Loaded;
        this.Next.Click += Next_Click;
    }

    private async void WelcomePage_Loaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // 加载配置文件，尝试自动获取Daemon API Key
        ConfigService.InitConfig();

        if (!string.IsNullOrEmpty(ConfigStore.DaemonApiKey))
        {
            // 已经有ApiKey，直接验证
            await VerifyDaemonApiKey();
        }
        else
        {
            // 没有获取到ApiKey，让用户输入
            Next.IsVisible = true;
        }
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
                .WithTitle("API Key无效")
                .WithContent(new TextBlock
                {
                    Text = "请重新输入有效的Daemon API Key。",
                    FontSize = 14,
                })
                .WithActionButton("关闭", _ => { Next.IsVisible = true; }, true)
                .TryShow();
        }
    }

    private void Next_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SideMenuHelper.MainSideMenuHelper?.NavigateTo(new SukiSideMenuItem
        {
            Header = "链接守护程序",
            Icon = new MaterialIcon()
            {
                Kind = MaterialIconKind.LinkVariant,
            },
            IsContentMovable = false,
            PageContent = new LinkDaemonPage()
        },true);
        SideMenuHelper.MainSideMenuHelper?.NavigateRemove(this);
    }
}