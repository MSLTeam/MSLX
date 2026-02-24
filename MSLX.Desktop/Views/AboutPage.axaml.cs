using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using MSLX.Desktop.Models;
using MSLX.Desktop.Utils.API;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace MSLX.Desktop.Views;

public partial class AboutPage : UserControl
{
    public ObservableCollection<MemberModel> Developers { get; } = new();
    public ObservableCollection<MemberModel> Testers { get; } = new();

    public AboutPage()
    {
        InitializeComponent();
        DataContext = this;

        // 设置版本号
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        VersionNumText.Text = version?.ToString() ?? "Unknown";

        // 加载静态成员数据
        LoadStaticMembers();

        // 加载MSLAPI信息
        AttachedToVisualTree += async (s, e) =>
        {
            await FetchUpdateLogs();
            LoadMemberImages();
        };
    }

    private void LoadStaticMembers()
    {
        Developers.Add(new MemberModel { Name = "Weheal", Role = "Core Developer", AvatarUrl = "https://q.qlogo.cn/headimg_dl?dst_uin=2035582067&spec=640&img_type=jpg", Desc = "核心开发者" });
        Developers.Add(new MemberModel { Name = "xiaoyu", Role = "Core Developer", AvatarUrl = "https://q.qlogo.cn/headimg_dl?dst_uin=1791123970&spec=640&img_type=jpg", Desc = "核心开发者" });


        Testers.Add(new MemberModel { Name = "GuHanDuRen", Role = "Alpha Tester", AvatarUrl = "https://q.qlogo.cn/headimg_dl?dst_uin=2778318425&spec=640&img_type=jpg", Desc = "最早期内部功能测试" });
        Testers.Add(new MemberModel { Name = "MSLX Beta 群友们", Role = "Members", AvatarUrl = "https://p.qlogo.cn/gh/839645854/839645854/0", Desc = "感谢各位内测群的群友们！" });
    }

    private async Task FetchUpdateLogs()
    {
        try
        {
            LogLoading.IsVisible = true;
            LogEmptyText.IsVisible = false;
            LogItemsControl.ItemsSource = null;

            var (success, data, msg) = await MSLAPIService.GetJsonDataAsync("/query/changelogs", "data", new Dictionary<string, string>
            {
                { "software", "MSLX-Desktop" }
            });

            var logs = new List<UpdateLogModel>();

            if (success && data is JArray jsonArray)
            {
                foreach (var item in jsonArray)
                {
                    logs.Add(new UpdateLogModel
                    {
                        Version = item["version"]?.ToString() ?? "",
                        Time = item["time"]?.ToString() ?? "",
                        Changes = item["changes"]?.ToString() ?? ""
                    });
                }
            }

            if (logs.Count > 0)
            {
                LogItemsControl.ItemsSource = logs;
            }
            else
            {
                LogEmptyText.IsVisible = true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error fetching logs: {ex.Message}");
            LogEmptyText.IsVisible = true;
            LogEmptyText.Text = "加载失败";
        }
        finally
        {
            LogLoading.IsVisible = false;
        }
    }

    private async void LoadMemberImages()
    {
        using var client = new HttpClient();
        var allMembers = new List<MemberModel>();
        allMembers.AddRange(Developers);
        allMembers.AddRange(Testers);

        foreach (var member in allMembers)
        {
            if (string.IsNullOrEmpty(member.AvatarUrl)) continue;

            try
            {
                var data = await client.GetByteArrayAsync(member.AvatarUrl);
                using var stream = new MemoryStream(data);
                member.AvatarBitmap = new Bitmap(stream);
            }
            catch
            {
            }
        }
    }
}

