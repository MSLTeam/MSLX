using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Markup.Xaml;
using MSLX.Desktop.Utils;
using MSLX.Desktop.Utils.API;
using Newtonsoft.Json.Linq;
using SukiUI.Dialogs;
using SukiUI.Toasts;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace MSLX.Desktop.Views.CreateTunnel.MSLFrp;

public partial class CreateMSLFrpTunnel : UserControl
{
    private LoginControl? _loginControl;
    private MainPage? _mainPage;

    public CreateMSLFrpTunnel()
    {
        InitializeComponent();
    }

    private async void OnInitialized(object? sender, EventArgs e)
    {
        _loginControl = new LoginControl(this);
        //_mainPage = new MainPage(this, new MainPage.UserInfo());

        // 尝试自动登录
        var token = ConfigService.Config.ReadConfigKey("MSLUserToken")?.ToString();
        if (!string.IsNullOrEmpty(token))
        {
            MSLUserService.SetUserToken(token);
            await GetFrpInfoAsync();
        }
        else
        {
            ShowLoginPage();
        }
    }

    // 页面切换
    private void ShowLoginPage()
    {
        this.Content = _loginControl;
        //LoginGrid.IsVisible = true;
        //MainTabControl.IsVisible = false;
    }

    private void ShowMainPage()
    {
        this.Content = _mainPage;
        //LoginGrid.IsVisible = false;
        //MainTabControl.IsVisible = true;
    }

    // 获取信息
    public async Task GetFrpInfoAsync(HttpResponse? response = null)
    {
        try
        {
            var dialog = DialogService.DialogManager.CreateDialog()
                .WithTitle("登录中")
                .WithContent(new TextBlock { Text = "获取用户信息……" });
            dialog.TryShow();

            response ??= await MSLUserService.GetAsync("/frp/userInfo", null);

            DialogService.DialogManager.DismissDialog();

            Debug.WriteLine(response);

            if (!response.IsSuccess || response.Content == null)
            {
                DialogService.ToastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("获取用户信息失败，请重新登录")
                .WithContent((response.Exception as Exception)?.Message ?? string.Empty)
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Queue();
                ShowLoginPage();
                return;
            }

            JObject json = JObject.Parse(response.Content);
            if (json["code"]?.Value<int>() == 200)
            {
                // 用户信息
                string Username = json["data"]?["name"]?.Value<string>() ?? string.Empty;
                int userGroup = json["data"]?["user_group"]?.Value<int>() ?? 0;
                string UserGroup = userGroup == 6 ? "超级管理员"
                    : userGroup == 1 ? "高级会员"
                    : userGroup == 2 ? "超级会员" : "普通用户";
                string UserMaxTunnels = json["data"]?["maxTunnelCount"]?.Value<string>() ?? string.Empty;
                long outdated = json["data"]?["outdated"]?.Value<long>() ?? 0;
                string UserOutdated = outdated == 3749682420
                    ? "长期有效"
                    : outdated.ToString();

                DialogService.ToastManager.CreateToast()
                .OfType(NotificationType.Success)
                .WithTitle("登录成功！")
                .WithContent("成功登录到MSL Frp服务")
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Queue();
                _mainPage = new MainPage(this,new MainPage.UserInfo
                {
                    Username = Username,
                    UserGroup = UserGroup,
                    UserMaxTunnels = UserMaxTunnels,
                    UserOutdated = UserOutdated
                });
                ShowMainPage();

            }
            else
            {
                DialogService.ToastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("获取用户信息失败")
                .WithContent(json["msg"]?.ToString() ?? string.Empty)
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Queue();
                ShowLoginPage();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
            DialogService.DialogManager.DismissDialog();
            DialogService.ToastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("获取用户信息失败")
                .WithContent(ex.Message)
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Queue();
            ShowLoginPage();
        }
    }
}