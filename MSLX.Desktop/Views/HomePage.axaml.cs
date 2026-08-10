using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MSLX.Desktop.Models;
using MSLX.Desktop.Utils;
using MSLX.Desktop.Utils.API;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace MSLX.Desktop.Views;

public partial class HomePage : UserControl
{
    public HomePage()
    {
        InitializeComponent();
        this.Initialized += HomePage_Initialized;
        this.StartBtn.Click += StartBtn_Click;
        this.GithubBtn.Click += GithubBtn_Click;
        this.DocsBtn.Click += DocsBtn_Click;
        this.OpenPanelBtn.Click += OpenPanelBtn_Click;
    }

    private async void HomePage_Initialized(object? sender, EventArgs e)
    {
        InitLocalInfo();
        _ = FetchHitokotoAsync();
        _ = LoadNoticeAsync();
        _ = LoadSystemStatusAsync();
        _ = LoadInstanceCountAsync();

        Console.WriteLine("数据目录：" + ConfigService.GetAppDataPath());
        Console.WriteLine("设备ID: " + PlatformHelper.GetDeviceID());
        Debug.WriteLine(ConfigStore.Version);
        await UpdateService.UpdateDesktopApp();
    }

    private void InitLocalInfo()
    {
        string uname = !string.IsNullOrEmpty(Environment.UserName) ? Environment.UserName : "管理员";
        UsernameText.Text = uname;
        MainWindow.Instance?.UpdateUserHeader(uname, null);

        var asmVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        string verStr = asmVersion != null ? $"{asmVersion.Major}.{asmVersion.Minor}.{asmVersion.Build}" : ConfigStore.Version.ToString();
        DesktopVersionText.Text = $"v{verStr}";
        DaemonVersionText.Text = ConfigStore.DaemonVersion != new Version(0, 0, 0, 0) ? $"v{ConfigStore.DaemonVersion.Major}.{ConfigStore.DaemonVersion.Minor}.{ConfigStore.DaemonVersion.Build}" : "v0.0.0";

        NetVersionText.Text = RuntimeInformation.FrameworkDescription;
        HostnameText.Text = Environment.MachineName;
        OsTypeText.Text = $"{PlatformHelper.GetOS()} ({PlatformHelper.GetOSArch()})";
        OsVersionText.Text = RuntimeInformation.OSDescription;

        if (PlatformHelper.IsMacAppBundle())
        {
            ShowAppIntegrationStatus();
        }
    }

    private async Task FetchHitokotoAsync()
    {
        const string fallbackMsg = "Every little creature in the world has their own piece of paradise.\nPlants take root in the boundless earth, and their leaves grow toward the stars.";
        try
        {
            var response = await HttpService.GetAsync("https://v1.hitokoto.cn/?c=a&c=b&c=c&c=d");
            if (response.IsSuccess && !string.IsNullOrEmpty(response.Content))
            {
                var json = JObject.Parse(response.Content);
                string hitokoto = json["hitokoto"]?.ToString() ?? "";
                string from = json["from"]?.ToString() ?? "";
                string fromWho = json["from_who"]?.ToString() ?? "";

                if (!string.IsNullOrEmpty(hitokoto))
                {
                    string authorStr = !string.IsNullOrEmpty(fromWho) ? $" ({fromWho})" : "";
                    HitokotoText.Text = $"{hitokoto} —— 《{from}》{authorStr}";
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"获取一言(Hitokoto)失败: {ex.Message}");
        }
        HitokotoText.Text = fallbackMsg;
    }

    private async Task LoadAvatarAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var data = await client.GetByteArrayAsync(url);
            using var stream = new MemoryStream(data);
            var bitmap = new Bitmap(stream);

            Dispatcher.UIThread.Post(() =>
            {
                var ellipse = this.FindControl<Ellipse>("AvatarEllipse");
                if (ellipse != null)
                {
                    ellipse.Fill = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
                    ellipse.IsVisible = true;
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"加载头像失败: {ex.Message}");
        }
    }

    private async Task LoadNoticeAsync()
    {
        try
        {
            var (Success, Data, Message) = await MSLAPIService.GetJsonDataAsync("/software/notice", queryParameters: new Dictionary<string, string> { { "query", "mslxNotice" } });
            if (Data == null || Message == null)
            {
                MarkdownViewer.ChangeMarkdownContent("暂无公告");
                return;
            }
            if (Success)
            {
                MarkdownViewer.ChangeMarkdownContent(Data.ToString() ?? "暂无公告");
            }
            else
            {
                MarkdownViewer.ChangeMarkdownContent(Message);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"加载公告失败: {ex.Message}");
            MarkdownViewer.ChangeMarkdownContent("加载公告失败");
        }
    }

    private async Task LoadSystemStatusAsync()
    {
        try
        {
            var (isSuccess, msg, data) = await DaemonAPIService.VerifyDaemonApiKey();
            if (isSuccess && data != null)
            {
                string avatarUrl = data["avatar"]?.Value<string>() ?? data["userInfo"]?["avatar"]?.Value<string>() ?? "";
                if (!string.IsNullOrEmpty(avatarUrl))
                {
                    _ = LoadAvatarAsync(avatarUrl);
                }

                string uname = data["name"]?.Value<string>() ?? data["user"]?.Value<string>() ?? data["username"]?.Value<string>() ?? "";
                if (!string.IsNullOrEmpty(uname))
                {
                    UsernameText.Text = uname;
                }

                if (!string.IsNullOrEmpty(uname) || !string.IsNullOrEmpty(avatarUrl))
                {
                    MainWindow.Instance?.UpdateUserHeader(uname, avatarUrl);
                }

                string daemonVer = data["version"]?.Value<string>() ?? "";
                if (!string.IsNullOrEmpty(daemonVer))
                {
                    DaemonVersionText.Text = daemonVer.StartsWith("v") ? daemonVer : $"v{daemonVer}";
                }

                if (data["systemInfo"] is JObject sysInfo)
                {
                    string netVer = sysInfo["netVersion"]?.Value<string>() ?? "";
                    if (!string.IsNullOrEmpty(netVer)) NetVersionText.Text = netVer;

                    string hostname = sysInfo["hostname"]?.Value<string>() ?? "";
                    if (!string.IsNullOrEmpty(hostname)) HostnameText.Text = hostname;

                    string osType = sysInfo["osType"]?.Value<string>() ?? "";
                    string osArch = sysInfo["osArchitecture"]?.Value<string>() ?? "";
                    if (!string.IsNullOrEmpty(osType)) OsTypeText.Text = string.IsNullOrEmpty(osArch) ? osType : $"{osType} ({osArch})";

                    string osVer = sysInfo["osVersion"]?.Value<string>() ?? "";
                    if (!string.IsNullOrEmpty(osVer)) OsVersionText.Text = osVer;
                }

                if (PlatformHelper.IsMacAppBundle())
                {
                    ShowAppIntegrationStatus();
                }
                else
                {
                    var targetVerObj = data["targetFrontendVersion"] as JObject;
                    string targetDesktopVerStr = targetVerObj?["desktop"]?.Value<string>() ?? "";

                    var currentAsmVer = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                    string currentVerStr = currentAsmVer != null ? $"{currentAsmVer.Major}.{currentAsmVer.Minor}.{currentAsmVer.Build}" : ConfigStore.Version.ToString();

                    if (!string.IsNullOrEmpty(targetDesktopVerStr))
                    {
                        bool isMatch = currentVerStr.StartsWith(targetDesktopVerStr);
                        if (isMatch)
                        {
                            VersionMatchBadge.Background = new SolidColorBrush(Color.Parse("#10B981"));
                            VersionMatchText.Text = "正确匹配";
                        }
                        else
                        {
                            VersionMatchBadge.Background = new SolidColorBrush(Color.Parse("#EF4444"));
                            VersionMatchText.Text = "请更新";
                        }
                    }
                    else
                    {
                        VersionMatchBadge.Background = new SolidColorBrush(Color.Parse("#10B981"));
                        VersionMatchText.Text = "正确匹配";
                    }
                }
            }
            else if (!PlatformHelper.IsMacAppBundle())
            {
                VersionMatchBadge.Background = new SolidColorBrush(Color.Parse("#6B7280"));
                VersionMatchText.Text = "未连接";
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"获取系统状态失败: {ex.Message}");
            if (!PlatformHelper.IsMacAppBundle())
            {
                VersionMatchBadge.Background = new SolidColorBrush(Color.Parse("#6B7280"));
                VersionMatchText.Text = "未连接";
            }
        }
    }

    private void ShowAppIntegrationStatus()
    {
        VersionMatchBadge.Background = new SolidColorBrush(Color.Parse("#10B981"));
        VersionMatchText.Text = "App集成";
    }

    private async Task LoadInstanceCountAsync()
    {
        try
        {
            await InstanceListPage.LoadServersList();
            int total = InstanceModel.Current.ServerList.Count;
            int online = InstanceModel.Current.ServerList.Count(s => s.Status != 0);
            OnlineInstancesText.Text = $"{online} / {total}";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"获取实例数量失败: {ex.Message}");
        }
    }

    private void OnlineInstances_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        SideMenuHelper.Current?.NavigateTo<InstanceListPage>();
    }

    private void StartBtn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SideMenuHelper.Current?.NavigateTo<InstanceListPage>();
    }

    private void GithubBtn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://github.com/MSLTeam/MSLX") { UseShellExecute = true });
    }

    private void DocsBtn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://mslx.mslmc.cn") { UseShellExecute = true });
    }

    private async void OpenPanelBtn_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var response = await DaemonAPIService.PostApiAsync(
                "/api/auth/browser-launch",
                null,
                HttpService.PostContentType.Json,
                null);

            if (!response.IsSuccess || string.IsNullOrWhiteSpace(response.Content))
            {
                throw new InvalidOperationException("无法创建浏览器登录令牌。");
            }

            var result = JObject.Parse(response.Content);
            if (result["code"]?.Value<int>() != 200)
            {
                throw new InvalidOperationException(result["message"]?.ToString() ?? "无法创建浏览器登录令牌。");
            }

            string? launchToken = result["data"]?["token"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(launchToken))
            {
                throw new InvalidOperationException("Daemon 返回的浏览器登录令牌为空。");
            }

            string address = ConfigStore.DaemonAddress.TrimEnd('/');
            string launchUrl = $"{address}/api/auth/browser-launch?token={Uri.EscapeDataString(launchToken)}";
            Process.Start(new ProcessStartInfo(launchUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"打开控制台失败: {ex.Message}");
        }
    }
}
