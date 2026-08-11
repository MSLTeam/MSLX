using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Interactivity;
using MSLX.Desktop.Models;
using MSLX.Desktop.Utils;
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
    public ObservableCollection<MemberModel> Contributors { get; } = new();
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
        
        Contributors.Add(new MemberModel { Name = "CoZooo", Role = "Contributors", AvatarUrl = "https://hk-gh.mslmc.cn/https://avatars.githubusercontent.com/u/57851661?v=4", Desc = "帮忙做了Homebrew的发布适配" });
        Contributors.Add(new MemberModel { Name = "chaoji233", Role = "Contributors", AvatarUrl = "https://hk-gh.mslmc.cn/https://avatars.githubusercontent.com/u/126066634?s=80&v=4", Desc = "帮忙优化了一些功能，重构了Chmlfrp部分" });
        Contributors.Add(new MemberModel { Name = "Hongbro886", Role = "Contributors", AvatarUrl = "https://hk-gh.mslmc.cn/https://avatars.githubusercontent.com/u/185684679?s=80&v=4", Desc = "帮忙修了一些bug" });
        Contributors.Add(new MemberModel { Name = "alright-qwq", Role = "Contributors", AvatarUrl = "https://hk-gh.mslmc.cn/https://avatars.githubusercontent.com/u/151932943?s=48&v=4", Desc = "帮忙在MSLX中完成对MCDR的适配" });
        Contributors.Add(new MemberModel { Name = "LegendarySHT", Role = "Contributors", AvatarUrl = "https://hk-gh.mslmc.cn/https://avatars.githubusercontent.com/u/198100090?s=80&v=4", Desc = "优化了地图渲染功能" });


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

            var (success, data, msg) = await MSLAPIService.GetJsonDataAsync("/software/changelogs", "data", new Dictionary<string, string>
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

    private async void OnCheckDesktopUpdateClick(object? sender, RoutedEventArgs e)
    {
        BtnCheckDesktopUpdate.IsEnabled = false;
        CheckDesktopUpdateText.Text = "正在检查...";

        try
        {
            if (PlatformHelper.IsMacAppBundle())
            {
                if (App.Instance != null)
                {
                    await App.Instance.CheckForMacAppUpdatesAsync();
                }
            }
            else
            {
                await UpdateService.UpdateDesktopApp();
            }
        }
        finally
        {
            BtnCheckDesktopUpdate.IsEnabled = true;
            CheckDesktopUpdateText.Text = "检查更新";
        }
    }

    private async void LoadMemberImages()
    {
        using var client = new HttpClient();
        var allMembers = new List<MemberModel>();
        allMembers.AddRange(Developers);
        allMembers.AddRange(Contributors);
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
