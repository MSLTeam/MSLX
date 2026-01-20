using Avalonia.Controls;
using Material.Icons;
using Material.Icons.Avalonia;
using MSLX.Desktop.Models;
using MSLX.Desktop.Utils;
using SukiUI.Controls;
using SukiUI.Dialogs;
using SukiUI.Toasts;
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
        ConfigService.GetDaemonApiKey();

        if (!string.IsNullOrEmpty(ConfigStore.DaemonApiKey))
        {
            var (Success, Msg) = await DaemonManager.StartDaemon(ConfigService.GetAppDataPath());
            if(!Success)
            {
                Next.IsVisible = true;
                return;
            }

            DialogService.DialogManager.CreateDialog()
                .WithTitle("正在启动守护程序")
                .WithContent("检测到MSLXData目录中的守护程序\n正在自动启动。")
                .TryShow();
            await Task.Delay(5000);
            DialogService.DialogManager.DismissDialog();

            // 已经有ApiKey，直接验证
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
                Next.IsVisible = true;
            }
        }
        else
        {
            // 没有获取到ApiKey，让用户输入
            Next.IsVisible = true;
        }
    }

    private void Next_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SideMenuHelper.MainSideMenuHelper?.NavigateRemove(this);
        SideMenuHelper.MainSideMenuHelper?.NavigateTo(new SukiSideMenuItem
        {
            Header = "下载守护程序",
            Icon = new MaterialIcon()
            {
                Kind = MaterialIconKind.Download,
            },
            IsContentMovable = false,
            PageContent = new DownloadDaemonPage()
        },true);
        
    }
}