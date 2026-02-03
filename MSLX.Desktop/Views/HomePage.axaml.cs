using Avalonia.Controls;
using MSLX.Desktop.Utils;
using MSLX.Desktop.Utils.API;
using Newtonsoft.Json.Linq;
using SukiUI.Controls;
using SukiUI.Dialogs;
using SukiUI.Toasts;
using System;
using System.Collections.Generic;

namespace MSLX.Desktop.Views;

public partial class HomePage : UserControl
{
    public HomePage()
    {
        InitializeComponent();
        // this.Loaded += HomePage_Loaded;
        this.Initialized += HomePage_Initialized;
        this.StartBtn.Click += StartBtn_Click;
        this.GithubBtn.Click += GithubBtn_Click;
        this.DocsBtn.Click += DocsBtn_Click;
    }

    private async void HomePage_Initialized(object? sender, EventArgs e)
    {
        var (Success, Data, Message) = await MSLAPIService.GetJsonDataAsync("/query/notice", queryParameters: new Dictionary<string, string> { { "query", "mslxNotice" } });
        if (Data == null || Message == null)
        {
            AnnouncementViewer.Markdown = "暂无公告";
            return;
        }
        if (Success)
        {
            AnnouncementViewer.Markdown = ((JObject)Data)["mslxNotice"]?.ToString() ?? "暂无公告";
        }
        else
        {
            AnnouncementViewer.Markdown = Message;
        }
        Console.WriteLine("数据目录：" + ConfigService.GetAppDataPath());
        Console.WriteLine("设备ID: " + PlatformHelper.GetDeviceID());
    }

    /*
    private async void HomePage_Loaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        
    }
    */

    private void StartBtn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DialogService.DialogManager.CreateDialog()
            .OfType(Avalonia.Controls.Notifications.NotificationType.Information)
            .WithTitle("Hello")
            .WithContent("你好")
            .WithActionButton("保持打开", _ => { })
            .WithActionButton("关闭", _ => { }, true)
            .TryShow();
    }

    private void GithubBtn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DialogService.ToastManager.CreateToast()
            .OfType(Avalonia.Controls.Notifications.NotificationType.Success)
            .WithTitle("已打开GitHub页面")
            .WithContent("感谢您的支持！")
            .Dismiss().After(TimeSpan.FromSeconds(3))
            .WithActionButton("Dismiss", _ => { }, true)
            .Queue();
    }

    private void DocsBtn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // SukiTheme.GetInstance().ChangeColorTheme(SukiColor.Orange);
        // SukiTheme.GetInstance().SwitchBaseTheme();
        SideMenuHelper.MainSideMenuHelper?.NavigateTo(new SukiSideMenuItem
        {
            Header = "123",
            Icon = new Material.Icons.Avalonia.MaterialIcon()
            {
                Kind = Material.Icons.MaterialIconKind.Home,
            },
            PageContent = new HomePage(),
            IsContentMovable = false,
        }, true);
    }
}