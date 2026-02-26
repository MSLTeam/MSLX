using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MSLX.Desktop.Models;
using MSLX.Desktop.Utils;     // 确保你的 DialogService 在这里
using MSLX.Desktop.Utils.API;
using Newtonsoft.Json.Linq;
using SukiUI.Toasts;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace MSLX.Desktop.Views;

public partial class SettingsPage : UserControl
{
    private SettingsModel _currentSettings = new SettingsModel();
    private bool _isLoading = false;

    public SettingsPage()
    {
        InitializeComponent();
        SwitchFirewall.IsCheckedChanged += (s, e) => UpdateFirewallText(SwitchFirewall.IsChecked == true);

        Loaded += async (s, e) => await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        if (_isLoading) return;
        SetLoadingState(true);

        try
        {
            var result = await DaemonAPIService.GetJsonDataAsync("/api/settings");

            if (result.Success && result.Data is JToken jsonToken)
            {
                _currentSettings = jsonToken.ToObject<SettingsModel>() ?? new SettingsModel();
                MapModelToUi();
            }
            else
            {
                ShowToast("加载失败", result.Msg ?? "无法获取设置", NotificationType.Error);
            }
        }
        catch (Exception ex)
        {
            ShowToast("错误", ex.Message, NotificationType.Error);
        }
        finally
        {
            SetLoadingState(false);
        }
    }

    private void MapModelToUi()
    {
        // Web Console
        SwitchWebConsole.IsChecked = _currentSettings.OpenWebConsoleOnLaunch;

        // Mirrors (通过 Tag 匹配)
        ComboMirrors.SelectedItem = null;

        foreach (var item in ComboMirrors.Items)
        {
            if (item is ComboBoxItem cbi && cbi.Tag?.ToString() == _currentSettings.NeoForgeInstallerMirrors)
            {
                ComboMirrors.SelectedItem = item;
                break;
            }
        }

        // 默认选第二个
        if (ComboMirrors.SelectedItem == null && ComboMirrors.ItemCount > 1)
        {
            ComboMirrors.SelectedIndex = 1;
        }

        // Firewall
        SwitchFirewall.IsChecked = _currentSettings.FireWallBanLocalAddr;
        UpdateFirewallText(_currentSettings.FireWallBanLocalAddr);

        // Host & Port
        TxtListenHost.Text = _currentSettings.ListenHost;
        NumListenPort.Value = _currentSettings.ListenPort;
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        SetLoadingState(true);

        try
        {
            _currentSettings.OpenWebConsoleOnLaunch = SwitchWebConsole.IsChecked ?? true;

            if (ComboMirrors.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag != null)
            {
                _currentSettings.NeoForgeInstallerMirrors = selectedItem.Tag.ToString()!;
            }

            _currentSettings.FireWallBanLocalAddr = SwitchFirewall.IsChecked ?? false;
            _currentSettings.ListenHost = TxtListenHost.Text ?? "localhost";
            _currentSettings.ListenPort = (int)(NumListenPort.Value ?? 1027);

            // 提交数据
            var response = await DaemonAPIService.PostApiAsync(
                "/api/settings", null, HttpService.PostContentType.Json, _currentSettings);

            if (response.IsSuccess)
            {
                ShowToast("保存成功", "系统设置已更新", NotificationType.Success);
            }
            else
            {
                string msg = "保存失败";
                try
                {
                    if (!string.IsNullOrEmpty(response.Content))
                        msg = JObject.Parse(response.Content)["message"]?.ToString() ?? msg;
                }
                catch { }
                ShowToast("保存失败", msg, NotificationType.Error);
            }
        }
        catch (Exception ex)
        {
            ShowToast("异常", ex.Message, NotificationType.Error);
        }
        finally
        {
            SetLoadingState(false);
        }
    }

    private void UpdateFirewallText(bool isChecked)
    {
        TxtFirewallStatus.Text = isChecked ? "已开启" : "已关闭";
    }

    private void SetLoadingState(bool isLoading)
    {
        _isLoading = isLoading;

        LoadingBar.IsVisible = isLoading;
        BtnSave.IsEnabled = !isLoading;
        BtnRefresh.IsEnabled = !isLoading;
        TxtListenHost.IsEnabled = !isLoading;
    }

    private void ShowToast(string title, string content, NotificationType type)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            DialogService.ToastManager.CreateToast()
                .OfType(type)
                .WithTitle(title)
                .WithContent(content)
                .Dismiss().After(TimeSpan.FromSeconds(3))
                .Queue();
        });
    }

    private async void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        await LoadDataAsync();
        ShowToast("刷新成功", "设置数据已重新加载", NotificationType.Success);
    }

    private async void OnCheckUpdateClick(object? sender, RoutedEventArgs e)
    {
        await UpdateService.UpdateDesktopApp();
    }

    private void OnRemoteAccessHelpClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://mslx.mslmc.cn/docs/config/remote-access/") { UseShellExecute = true });
        }
        catch { }
    }
}