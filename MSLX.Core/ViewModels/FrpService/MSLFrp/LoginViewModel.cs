using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MSLX.Core.Utils;
using Newtonsoft.Json.Linq;
using SukiUI.Controls;
using SukiUI.Dialogs;
using SukiUI.Toasts;

namespace MSLX.Core.ViewModels.FrpService.MSLFrp
{
    public partial class LoginViewModel : ViewModelBase
    {
        private MSLFrpViewModel FatherViewModel { get; set; } = new MSLFrpViewModel(); // 父级视图模型
        public LoginViewModel(MSLFrpViewModel mslFrpViewModel)
        {
            FatherViewModel = mslFrpViewModel;
        }

        // Design time data
        public LoginViewModel()
        {
            FatherViewModel = new MSLFrpViewModel();
        }

        // 登录相关
        [ObservableProperty]
        private string _account;
        [ObservableProperty]
        private string _password;
        [ObservableProperty]
        private bool _isSaveLoginStatus;
        private string token;

        
        [RelayCommand]
        private async Task LoginByPassword()
        {
            if (!string.IsNullOrEmpty(Password) && !string.IsNullOrEmpty(Account))
            {
                try
                {
                    // 添加登录中弹窗
                    var dialog = MainViewModel.DialogManager.CreateDialog()
                        .WithTitle("登录中")
                        .WithContent(new TextBlock { Text = "正在登录，请稍候..." });
                    dialog.TryShow();

                    HttpService.HttpResponse response = await MSLUser.PostAsync("/user/login", HttpService.PostContentType.Json, new
                    {
                        email = Account,
                        password = Password
                    });
                    // 关闭登录中弹窗
                    MainViewModel.DialogManager.DismissDialog();
                    JObject json = JObject.Parse(response.Content);
                    if (json["code"]?.Value<int>() == 200)
                    {
                        Console.WriteLine("登录成功！");
                        token = json["data"]["token"].Value<string>();
                        if (IsSaveLoginStatus)
                        {
                            ConfigService.Config.WriteConfigKey("MSLUserToken", token);
                        }
                        FatherViewModel.UserToken = token ?? string.Empty;
                        await FatherViewModel.GetFrpInfoAsync();
                    }
                    else
                    {
                        MessageService.ShowToast("登录失败", json["msg"]?.Value<string>() ?? "未知错误！", NotificationType.Error);
                        Debug.WriteLine(json["msg"]?.Value<string>() ?? string.Empty);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                    MainViewModel.DialogManager.DismissDialog();
                }
            }
            else
            {
                MessageService.ShowToast("错误", "请填写账户密码。", NotificationType.Error);
            }
        }
        
        [RelayCommand]
        private void OpenRegisterWeb()
        {
            var url = "https://user.mslmc.net/register";
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }
}