using Avalonia.Controls;
using Avalonia.Threading;
using Material.Icons;
using Material.Icons.Avalonia;
using MSLX.Desktop.Models;
using MSLX.Desktop.Utils;
using SukiUI.Controls;
using SukiUI.Dialogs;
using SukiUI.Toasts;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;

namespace MSLX.Desktop.Views.LinkDaemon;

public partial class WelcomePage : UserControl
{
    // 日志数据源
    private readonly ObservableCollection<string> _logLines = new();

    public WelcomePage()
    {
        InitializeComponent();

        LogListBox.ItemsSource = _logLines;

        this.Loaded += WelcomePage_Loaded;
        this.Retry.Click += Retry_Click;
        this.Next.Click += Next_Click;

        DaemonManager.DaemonLogReceived += OnDaemonLogReceived;
    }

    private void OnDaemonLogReceived(string log)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _logLines.Add(log);

            // 日志过多时丢弃最旧的行，控制集合大小
            while (_logLines.Count > 500)
            {
                _logLines.RemoveAt(0);
            }

            // 新日志到达后自动滚动到底部
            if (_logLines.Count > 0)
            {
                LogScroll.ScrollToEnd();
            }
        });
    }

    // 展开日志面板：切换 Class，具体的高度/透明度动画由 Styles 里的 Transition 驱动
    private void ShowLogPanel()
    {
        Dispatcher.UIThread.Post(() =>
        {
            LogContainer.Classes.Add("expanded");
        });
    }

    // 收起日志面板：移除 Class
    private void HideLogPanel()
    {
        Dispatcher.UIThread.Post(() =>
        {
            LogContainer.Classes.Remove("expanded");
        });
    }

    // Method A: 载入Welcome界面后，读取MSLX.Desktop配置（若检测失败，进入Method B），检测是否存储Address和ApiKey，若有则进入验证方法，没有则进入Method B
    // 验证: 读取配置文件是否写有AutoRunDaemon标记，若有则尝试启动守护程序（即MSLXData目录下的守护程序，若启动失败则进入Method C），
    // 并验证ApiKey，成功跳转主页面，失败进入Method D
    // 若配置文件无AutoRunDaemon标记，则直接验证ApiKey，成功跳转主页面，失败进入Method D

    // Method B: 检测MSLXData目录下的Daemon程序，若存在则尝试启动Daemon程序并尝试获取ApiKey进行验证，验证成功跳转主页面，失败进入Method C
    // Method C: 显示“下一步”按钮，用户点击后跳转到下载守护程序页面
    // Method D: 显示“下一步”按钮，用户点击后跳转到链接守护程序页面
    // Method E: 显示“Retry”按钮

    // 上述说明仅供参考，因为后续写代码时对部分不完善的逻辑进行了补充调整，请以代码逻辑为准。

    private async Task MethodA()
    {
        Debug.WriteLine("WelcomePage: MethodA Start");

        string daemonAddress = ConfigService.Config.ReadConfigKey("DaemonAddress")?.ToString() ?? string.Empty;
        string daemonApiKey = ConfigService.Config.ReadConfigKey("DaemonApiKey")?.ToString() ?? string.Empty;

        bool autoRunDaemon = ConfigService.Config.ReadConfigKey("AutoRunDaemon")?.ToObject<bool>() ?? false;

        if (!string.IsNullOrEmpty(daemonAddress) && !string.IsNullOrEmpty(daemonApiKey))
        {
            ConfigStore.DaemonAddress = daemonAddress;
            ConfigStore.DaemonApiKey = daemonApiKey;
            await Task.Delay(150);
            if (DaemonManager.FindDaemonProcess() == null)
            {
                if (autoRunDaemon)
                {
                    // 尝试启动守护程序
                    ShowLogPanel();
                    var (Success, Msg) = await DaemonManager.StartDaemon(ConfigService.GetAppDataPath());
                    if (Success)
                    {
                        // 启动成功，尝试验证
                        var (isSuccess, _) = await DaemonManager.VerifyDaemonApiKey();
                        if (isSuccess)
                        {
                            // 验证成功，跳转到主页面
                            SideMenuHelper.Current?.ShowMainPages();
                            SideMenuHelper.Current?.NavigateRemove(this);
                            SideMenuHelper.Current?.NavigateTo<HomePage>();

                            _ = UpdateService.UpdateDaemonApp(false);
                        }
                        else
                        {
                            MethodD();
                            MethodE();
                        }
                    }
                    else
                    {
                        MethodC();
                    }
                }
                else
                {
                    if (await DaemonManager.GetKeyAndLinkDaemon(false, false))
                    {
                        SideMenuHelper.Current?.ShowMainPages();
                        SideMenuHelper.Current?.NavigateRemove(this);
                        SideMenuHelper.Current?.NavigateTo<HomePage>();
                    }
                    else
                    {
                        MethodD();
                    }
                }
            }
            else
            {
                // 直接验证
                var (isSuccess, _) = await DaemonManager.VerifyDaemonApiKey();
                if (isSuccess)
                {
                    // 验证成功，跳转到主页面
                    SideMenuHelper.Current?.ShowMainPages();
                    SideMenuHelper.Current?.NavigateRemove(this);
                    SideMenuHelper.Current?.NavigateTo<HomePage>();

                    _ = UpdateService.UpdateDaemonApp(true);
                }
                else
                {
                    MethodD();
                    MethodE();
                }
            }
        }
        else
        {
            await MethodB();
        }
    }

    private async Task MethodB()
    {
        Debug.WriteLine("WelcomePage: MethodB Start");

        if (DaemonManager.FindDaemonProcess() != null)
        {
            MethodD();
        }
        else
        {
            ShowLogPanel();
            var (Success, Msg) = await DaemonManager.StartDaemon(ConfigService.GetAppDataPath());
            if (Success)
            {
                bool isSuccess = await DaemonManager.GetKeyAndLinkDaemon();
                if (isSuccess)
                {
                    // 验证成功，跳转到主页面
                    SideMenuHelper.Current?.ShowMainPages();
                    SideMenuHelper.Current?.NavigateRemove(this);
                    SideMenuHelper.Current?.NavigateTo<HomePage>();
                    return;
                }
            }
            MethodC();
        }
    }

    private void MethodC()
    {
        Debug.WriteLine("WelcomePage: MethodC Start");
        HideLogPanel();
        Next.Tag = 0;
        Next.IsVisible = true;
    }

    private void MethodD()
    {
        Debug.WriteLine("WelcomePage: MethodD Start");
        HideLogPanel();
        Next.Tag = 1;
        Next.IsVisible = true;
    }

    private void MethodE()
    {
        Debug.WriteLine("WelcomePage: MethodE Start");
        Retry.IsVisible = true;
    }

    private async void WelcomePage_Loaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
#if DEBUG
        DialogService.ToastManager.CreateToast()
            .WithTitle("Debug")
            .WithContent("继续操作？")
            .WithActionButton("继续", async _ =>
            {
                await MethodA();
            }, true)
            .Queue();
        return;
#else
        await MethodA();
#endif
    }

    private void Next_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SideMenuHelper.Current?.NavigateRemove(this);
        int tag = (int)(Next.Tag ?? 0);
        if (tag == 0)
        {
            SideMenuHelper.Current?.NavigateTo(new SukiSideMenuItem
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
        else
        {
            SideMenuHelper.Current?.NavigateTo(new SukiSideMenuItem
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

    private async void Retry_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await MethodA();
    }
}