using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MSLX.Desktop.Utils;
using MSLX.Desktop.Utils.API;
using Newtonsoft.Json.Linq;
using SukiUI.Dialogs;
using SukiUI.Toasts;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace MSLX.Desktop.Views.CreateTunnel.MSLFrp;

public partial class LoginControl : UserControl
{
    private CreateMSLFrpTunnel FatherControl { get; set; }
    private string _userToken = string.Empty;

    public LoginControl(CreateMSLFrpTunnel fatherControl)
    {
        InitializeComponent();
        FatherControl = fatherControl;
    }

    // design time
    public LoginControl() : this(new CreateMSLFrpTunnel()) { }

    // 登录
    private async void LoginButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            DialogService.DialogManager.CreateDialog()
                .WithTitle("登录中")
                .WithContent("正在登录，请稍候...")
                .TryShow();

            Random rand = new Random();
            string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            string randomString = string.Empty;

            for (int i = 0; i < 32; i++)
            {
                randomString += chars[rand.Next(chars.Length)];
            }
            string csrf = randomString;
            var postData = new Dictionary<string, string>
            {
                { "csrf", csrf },
                { "appid", "eixl7BLlidSZ7POjdhZsAGTXKyu" }
            };

            var response = await MSLUserService.PostAsync(
                "/oauth/createAppLogin",
                HttpService.PostContentType.FormUrlEncoded,
                postData
            );

            if (response.IsSuccess && response.Content != null)
            {
                JObject _json = JObject.Parse(response.Content);
                if (_json["code"]?.Value<int>() != 200 || _json["data"] == null)
                {
                    return;
                }
                JObject data = _json["data"] as JObject ?? new JObject();
                string url = data["url"]?.Value<string>() ?? string.Empty;
                string ssid = data["ssid"]?.Value<string>() ?? string.Empty;

                if (string.IsNullOrEmpty(url) && string.IsNullOrEmpty(ssid))
                {
                    return;
                }
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                StartPolling(ssid, csrf); // 轮询
                return;
            }
            DialogService.DialogManager.DismissDialog();
            return;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
            DialogService.DialogManager.DismissDialog();
        }
    }

    private CancellationTokenSource? _pollingCts;
    private async void StartPolling(string ssid, string csrf)
    {
        _pollingCts = new CancellationTokenSource();
        var cancellationToken = _pollingCts.Token;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var response = await MSLUserService.GetAsync(
                    $"/oauth/appLogin?ssid={ssid}&csrf={csrf}", null

                );

                if (cancellationToken.IsCancellationRequested) return;
                JObject ContentInfo = JObject.Parse(response.Content);
                if (response.IsSuccess)
                {
                    var appToken = ContentInfo?["data"]?["token"]?.Value<string>();
                    if (!string.IsNullOrEmpty(appToken))
                    {
                        await CompleteBrowserLogin(appToken);
                        break; // 结束轮询
                    }
                    else
                    {
                        // 继续轮询
                        await Task.Delay(3000, cancellationToken);
                        continue;
                    }
                }
                else
                {
                    // 出现错误
                    // 取消轮询
                    _pollingCts?.Cancel();
                    break; // 结束轮询
                }
            }
        }
        catch (TaskCanceledException)
        {
            _pollingCts?.Cancel();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
            // 取消轮询
            _pollingCts?.Cancel();
        }
        finally
        {
            _pollingCts?.Dispose();
            _pollingCts = null;
        }
    }

    public async Task<(int Code, string Msg, HttpResponse? Response)> UserLogin(string token, bool saveToken = false)
    {
        try
        {
            HttpResponse res = await MSLUserService.GetAsync("/frp/userInfo", null);

            if (res.IsSuccess)
            {
                var loginRes = JObject.Parse(res.Content.ToString());
                if (loginRes["code"]?.Value<int>() != 200)
                {
                    return (loginRes["code"]?.Value<int>() ?? 0, loginRes["msg"]?.ToString() ?? string.Empty, null);
                }

                // 用户登陆成功后，发送POST请求续期Token
                _ = await HttpService.PostAsync("/user/renewToken", HttpService.PostContentType.None);
                return (200, string.Empty, res);
            }
            return (200, string.Empty, null);
        }
        catch (Exception ex)
        {
            return (0, ex.Message, null);
        }
    }

    private async Task CompleteBrowserLogin(string appToken)
    {
        Console.WriteLine(appToken);
        MSLUserService.SetUserToken(appToken);
        var (Code, Msg, Response) = await UserLogin(
            appToken,
            false
        );
        if (Code == 200)
        {
            _userToken = appToken;

            if (SaveLoginToggle.IsChecked == true)
                ConfigService.Config.WriteConfigKey("MSLUserToken", _userToken);

            MSLUserService.SetUserToken(_userToken);
            await FatherControl.GetFrpInfoAsync(Response);
            //await GetFrpInfoAsync(Response);
        }
        else
        {
            DialogService.DialogManager.DismissDialog();
            DialogService.ToastManager.CreateToast()
                .OfType(NotificationType.Error)
                .WithTitle("登录失败")
                .WithContent(Msg)
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Queue();
        }
    }

    private void RegisterButton_Click(object? sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://user.mslmc.net/register") { UseShellExecute = true });
    }
}