using Avalonia.Controls;
using Material.Icons;
using Material.Icons.Avalonia;
using MSLX.Desktop.Models;
using MSLX.Desktop.Utils;
using SukiUI.Controls;
using SukiUI.Dialogs;
using SukiUI.Toasts;
using System.Diagnostics;
using System.Threading.Tasks;

namespace MSLX.Desktop.Views.LinkDaemon;

public partial class WelcomePage : UserControl
{
    public WelcomePage()
    {
        InitializeComponent();

        this.Loaded += WelcomePage_Loaded;
        this.Retry.Click += Retry_Click;
        this.Next.Click += Next_Click;
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
                    var (Success, Msg) = await DaemonManager.StartDaemon(ConfigService.GetAppDataPath());
                    if (Success)
                    {
                        // 启动成功，尝试验证
                        bool isSuccess = await DaemonManager.VerifyDaemonApiKey();
                        if (isSuccess)
                        {
                            // 验证成功，跳转到主页面
                            SideMenuHelper.MainSideMenuHelper?.ShowMainPages();
                            SideMenuHelper.MainSideMenuHelper?.NavigateRemove(this);
                            SideMenuHelper.MainSideMenuHelper?.NavigateTo<HomePage>();
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
                    if (await DaemonManager.GetKeyAndLinkDaemon(false))
                    {
                        // 验证成功，跳转到主页面
                        SideMenuHelper.MainSideMenuHelper?.ShowMainPages();
                        SideMenuHelper.MainSideMenuHelper?.NavigateRemove(this);
                        SideMenuHelper.MainSideMenuHelper?.NavigateTo<HomePage>();
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
                bool isSuccess = await DaemonManager.VerifyDaemonApiKey();
                if (isSuccess)
                {
                    // 验证成功，跳转到主页面
                    SideMenuHelper.MainSideMenuHelper?.ShowMainPages();
                    SideMenuHelper.MainSideMenuHelper?.NavigateRemove(this);
                    SideMenuHelper.MainSideMenuHelper?.NavigateTo<HomePage>();
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

        var (Success, Msg) = await DaemonManager.StartDaemon(ConfigService.GetAppDataPath());
        if (Success)
        {
            DialogService.DialogManager.DismissDialog();
            DialogService.DialogManager.CreateDialog()
                .WithTitle("守护程序启动成功")
                .WithContent("已成功启动守护程序，正在尝试获取API Key进行验证。")
                .TryShow();
            await Task.Delay(2500);
            DialogService.DialogManager.DismissDialog();

            bool isSuccess = await DaemonManager.GetKeyAndLinkDaemon();
            if (isSuccess)
            {
                // 验证成功，跳转到主页面
                SideMenuHelper.MainSideMenuHelper?.ShowMainPages();
                SideMenuHelper.MainSideMenuHelper?.NavigateRemove(this);
                SideMenuHelper.MainSideMenuHelper?.NavigateTo<HomePage>();
                return;
            }
        }
        MethodC();
    }

    private void MethodC()
    {
        Debug.WriteLine("WelcomePage: MethodC Start");
        Next.Tag = 0;
        Next.IsVisible = true;
    }

    private void MethodD()
    {
        Debug.WriteLine("WelcomePage: MethodD Start");
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
        SideMenuHelper.MainSideMenuHelper?.NavigateRemove(this);
        int tag = (int)(Next.Tag ?? 0);
        if (tag == 0)
        {
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
        else
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
            }, true);
        }

    }

    private async void Retry_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await MethodA();
    }
}