using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Material.Icons;
using Material.Icons.Avalonia;
using MSLX.Desktop.Utils;
using MSLX.Desktop.Utils.API;
using Newtonsoft.Json.Linq;
using SukiUI.Controls;
using SukiUI.Dialogs;
using SukiUI.Toasts;
using System;

namespace MSLX.Desktop.Views.LinkDaemon;

public partial class DownloadDaemonPage : UserControl
{
    public DownloadDaemonPage()
    {
        InitializeComponent();

        // this.Loaded += DownloadDaemonPage_Loaded;
        this.DoneBtn.Click += DoneBtn_Click;
        this.LinkDaemonBtn.Click += LinkDaemonBtn_Click;
    }

    /*
    private void DownloadDaemonPage_Loaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {

    }
    */

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

            await DownloadManager.Instance.WaitForItemCompletionAsync(taskid);

            task = DownloadManager.Instance.GetDownloadItem(taskid);
            DialogService.DialogManager.CreateDialog()
                .WithTitle("下载完成")
                .WithContent(task?.Filename ?? "unknown")
                .WithActionButton("确定", _ => { }, true)
                .TryShow();
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

    private void LinkDaemonBtn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
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