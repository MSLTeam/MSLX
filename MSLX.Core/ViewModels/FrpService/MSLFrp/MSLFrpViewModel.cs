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
using static MSLX.Core.Models.FrpService.MSLFrpModel;

namespace MSLX.Core.ViewModels.FrpService.MSLFrp
{
    public partial class MSLFrpViewModel : ViewModelBase
    {
        public string UserToken { get; set; }
        
        [ObservableProperty]
        private object? _mainContent;

        public MSLFrpViewModel()
        {
            UserToken = string.Empty;
        }

        [RelayCommand]
        private async Task Inited()
        {

            var _token = ConfigService.Config.ReadConfigKey("MSLUserToken")?.ToString();
            if (!string.IsNullOrEmpty(_token))
            {
                UserToken = _token;
                await GetFrpInfoAsync();
            }
            else
            {
                MainContent = new LoginViewModel(this);
            }
        }

        public async Task GetFrpInfoAsync()
        {
            try
            {
                // ����Զ���¼�е���
                var dialog = MainViewModel.DialogManager.CreateDialog()
                    .WithTitle("��¼��")
                    .WithContent(new TextBlock { Text = "��ȡ�û���Ϣ����" });
                dialog.TryShow();

                // ��ȡMSL Frp�û���Ϣ
                HttpService.HttpResponse response = await MSLUser.GetAsync("/frp/userInfo", null, new Dictionary<string, string>()
                {
                    ["Authorization"] = $"Bearer {UserToken}"
                });
                MainViewModel.DialogManager.DismissDialog();
                JObject json = JObject.Parse(response.Content);
                if ((int)json["code"] == 200)
                {
                    MessageService.ShowToast("��¼�ɹ���", "�ɹ���¼��MSL Frp����", NotificationType.Success);

                    MainContent = new FrpMainViewModel(json)
                    {
                        UserToken = UserToken,
                    };
                }
                else
                {
                    MessageService.ShowToast("��ȡ�û���Ϣʧ��", (string)json["msg"], NotificationType.Error);
                    Debug.WriteLine((string)json["msg"]);
                    return;
                }

            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                MainViewModel.DialogManager.DismissDialog();
                //ShowMainPage = false;
            }
            
            return;
        }
    }
}