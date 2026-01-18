using Avalonia.Controls;
using MSLX.Desktop.Utils;
using MSLX.Desktop.Utils.API;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace MSLX.Desktop;

public partial class HomePage : UserControl
{
    public HomePage()
    {
        InitializeComponent();
        this.Loaded += HomePage_Loaded;
    }

    private async void HomePage_Loaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
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
}