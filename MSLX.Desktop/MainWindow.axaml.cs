using Avalonia.Controls;
using MSLX.Desktop.Models;
using MSLX.Desktop.Utils;
using MSLX.Desktop.Utils.API;
using Newtonsoft.Json.Linq;
using SukiUI.Controls;
using SukiUI.Dialogs;
using SukiUI.Toasts;
using System.Threading.Tasks;

namespace MSLX.Desktop;

public partial class MainWindow : SukiWindow
{
    public MainWindow()
    {
        InitializeComponent();

        this.Closing += MainWindow_Closing;

        this.DialogManager.Manager = DialogService.DialogManager;
        this.ToastManager.Manager = DialogService.ToastManager;
        SideMenuHelper.MainSideMenuHelper = new SideMenuHelper();
        SideMenuHelper.MainSideMenuHelper.SideMenu = this.MainSideMenu;
        this.MainSideMenu.ItemsSource = PageStore.MainPages;
        SideMenuHelper.MainSideMenuHelper?.HideMainPages(0);
    }

    private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        DaemonManager.StopRunningDaemon();
    }

    private void InputDaemonApiKey()
    {
        // 弹出对话框，让用户手动输入ApiKey
        // 创建输入框
        var ipInputBox = new TextBox
        {
            Text = ConfigStore.DaemonAddress.Replace("/api", string.Empty),
        };
        var keyInputBox = new TextBox
        {
            Watermark = "API Key",
        };

        // 定义对话框内容
        var dialogContent = new StackPanel
        {
            Margin = new Avalonia.Thickness(0, 10, 0, 10),
            Spacing = 5,
            Children =
                {
                    new TextBlock
                    {
                        Text="请输入MSLX Daemon的连接信息：",
                        FontWeight=Avalonia.Media.FontWeight.Bold,
                    },
                    new TextBlock
                    {
                        Text="IP地址：",
                        FontWeight=Avalonia.Media.FontWeight.Bold,
                        Margin=new Avalonia.Thickness(0,5,0,0),
                    },
                    ipInputBox,
                    new TextBlock
                    {
                        Text="API Key：",
                        FontWeight=Avalonia.Media.FontWeight.Bold,
                        Margin=new Avalonia.Thickness(0,5,0,0),
                    },
                    keyInputBox
                }
        };

        DialogService.DialogManager.DismissDialog();
        // 显示对话框
        DialogService.DialogManager.CreateDialog()
        .OfType(Avalonia.Controls.Notifications.NotificationType.Information)
        .WithTitle("欢迎使用！")
        .WithContent(dialogContent)
        .WithActionButton("确定", async _ =>
        {
            string ipText = ipInputBox.Text;
            if (!string.IsNullOrEmpty(ipText))
            {
                ipText += "/api";
                if (ipText != ConfigStore.DaemonAddress)
                    ConfigStore.DaemonAddress = ipText;
            }
            ConfigStore.DaemonApiKey = keyInputBox.Text ?? string.Empty; await VerifyDaemonApiKey();
        })
        .TryShow();
    }

    private async Task VerifyDaemonApiKey()
    {
        // 关闭先前对话框并显示验证中对话框
        DialogService.DialogManager.DismissDialog();
        DialogService.DialogManager.CreateDialog()
            .WithTitle("验证中...")
            .WithContent("请稍候，正在验证Daemon API Key的有效性。")
            .TryShow();
        // 开始验证
        var response = await DaemonAPIService.GetApiAsync("/status");
        DialogService.DialogManager.DismissDialog();
        if (response.IsSuccess)
        {
            var jsonContent = JObject.Parse(response.Content);
            if (jsonContent["code"]?.Value<int>() == 200)
            {
                string msg = jsonContent["message"]?.Value<string>() ?? "200";
                var data = jsonContent["data"] as JObject;
                string clientName = data?["clientName"]?.Value<string>() ?? "Unknown";
                string version = data?["version"]?.Value<string>() ?? "Unknown";
                string serverTime = data?["serverTime"]?.Value<string>() ?? "Unknown";

                DialogService.DialogManager.CreateDialog()
                    .WithTitle(msg)
                    .WithContent(new TextBlock
                    {
                        Text = $"Client Name: {clientName}\nVersion: {version}\nServer Time: {serverTime}",
                        FontSize = 14,
                    })
                    .WithActionButton("关闭", _ => { }, true)
                    .TryShow();
                return;

            }
        }
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
            .WithActionButton("关闭", _ => { InputDaemonApiKey(); }, true)
            .TryShow();
    }
}