using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MSLX.Desktop.Models;
using MSLX.SDK.Models.Settings;
using MSLX.Desktop.Utils;
using MSLX.Desktop.Utils.API;
using Newtonsoft.Json.Linq;
using SukiUI.Toasts;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Controls.ApplicationLifetimes;
using SukiUI;
using SukiUI.Models;

namespace MSLX.Desktop.Views;

public partial class SettingsPage : UserControl
{
    private UpdateSettingsRequest _currentSettings = new UpdateSettingsRequest();
    private bool _isLoading = false;

    private bool _isUiLoaded = false;

    public SettingsPage()
    {
        InitializeComponent();
        SwitchFirewall.IsCheckedChanged += (s, e) => UpdateFirewallText(SwitchFirewall.IsChecked == true);
        SwitchExternalAccess.IsCheckedChanged += (s, e) => UpdateExternalAccessText(SwitchExternalAccess.IsChecked == true);
        SliderDownloadThreadCount.ValueChanged += (s, e) => UpdateDownloadThreadUi((int)e.NewValue);

        ApplyDaemonBundleRestrictions();

        LoadLocalSettings();

        Loaded += async (s, e) => await LoadDataAsync();
    }

    /// <summary>
    /// 内置 Daemon 由 Desktop 管理时，固定本地连接和监听地址。
    /// </summary>
    private void ApplyDaemonBundleRestrictions()
    {
        if (!PlatformHelper.IsMacAppBundle())
        {
            return;
        }

        FirewallSettingRow.IsVisible = false;
        FirewallSettingSeparator.IsVisible = false;
        ExternalAccessSettingRow.IsVisible = true;
        ExternalAccessSettingSeparator.IsVisible = true;
        TxtListenSettingTitle.Text = "监听端口";
        TxtListenSettingDescription.Text = "设置内置守护进程的监听端口，修改后需重启";
        TxtListenHost.IsVisible = false;
        TxtListenHostSeparator.IsVisible = false;
        TxtListenHost.Text = "localhost";
        TxtListenHost.IsReadOnly = true;
        TxtListenHost.IsEnabled = false;
    }

    private void LoadLocalSettings()
    {
        var themeColor = ConfigService.Config.ReadConfigKey("ThemeColor")?.ToString() ?? "Blue";
        foreach (var item in ComboThemeColor.Items)
        {
            if (item is ComboBoxItem cbi && cbi.Tag?.ToString() == themeColor)
            {
                ComboThemeColor.SelectedItem = item;
                break;
            }
        }

        var themeMode = ConfigService.Config.ReadConfigKey("ThemeMode")?.ToString() ?? "System";
        foreach (var item in ComboThemeMode.Items)
        {
            if (item is ComboBoxItem cbi && cbi.Tag?.ToString() == themeMode)
            {
                ComboThemeMode.SelectedItem = item;
                break;
            }
        }

        ComboBackgroundTarget.SelectedIndex = 0;
        LoadBackgroundSettings();

        _isUiLoaded = true;
    }

    private string GetConfigKey(string baseKey)
    {
        if (ComboBackgroundTarget?.SelectedItem is ComboBoxItem item && item.Tag?.ToString() is string tag && tag != "Global")
        {
            return $"{baseKey}_{tag}";
        }
        return baseKey;
    }

    private void LoadBackgroundSettings()
    {
        _isUiLoaded = false;
        
        if (double.TryParse(ConfigService.Config.ReadConfigKey(GetConfigKey("BackgroundOpacity"))?.ToString(), out var opacity))
        {
            SliderBackgroundOpacity.Value = opacity;
        }
        else
        {
            SliderBackgroundOpacity.Value = 0.8;
        }

        if (double.TryParse(ConfigService.Config.ReadConfigKey(GetConfigKey("BackgroundBlur"))?.ToString(), out var blur))
        {
            SliderBackgroundBlur.Value = blur;
        }
        else
        {
            SliderBackgroundBlur.Value = 0;
        }
        
        _isUiLoaded = true;
    }

    private void OnBackgroundTargetChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_isUiLoaded) return;
        LoadBackgroundSettings();
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
                _currentSettings = jsonToken.ToObject<UpdateSettingsRequest>() ?? new UpdateSettingsRequest();
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

        // 内置 Daemon 不读取或使用禁止本地访问配置。
        if (!PlatformHelper.IsMacAppBundle())
        {
            SwitchFirewall.IsChecked = _currentSettings.FireWallBanLocalAddr;
            UpdateFirewallText(_currentSettings.FireWallBanLocalAddr);
        }

        if (PlatformHelper.IsMacAppBundle())
        {
            SwitchExternalAccess.IsChecked = _currentSettings.AllowExternalAccess;
            UpdateExternalAccessText(_currentSettings.AllowExternalAccess);
        }

        // Host & Port
        TxtListenHost.Text = PlatformHelper.IsMacAppBundle() ? "localhost" : _currentSettings.ListenHost;
        NumListenPort.Value = _currentSettings.ListenPort;

        // Download Thread Count
        SliderDownloadThreadCount.Value = _currentSettings.DownloadThreadCount;
        UpdateDownloadThreadUi(_currentSettings.DownloadThreadCount);
    }

    private void UpdateDownloadThreadUi(int val)
    {
        TxtDownloadThreadCountVal.Text = val.ToString();
        TxtDownloadThreadWarning.IsVisible = val > 5;
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

            if (PlatformHelper.IsMacAppBundle())
            {
                // 内置 Daemon 固定 localhost，防止错误配置影响 Desktop 连接。
                _currentSettings.FireWallBanLocalAddr = false;
                _currentSettings.ListenHost = "localhost";
                _currentSettings.AllowExternalAccess = SwitchExternalAccess.IsChecked ?? false;
            }
            else
            {
                _currentSettings.FireWallBanLocalAddr = SwitchFirewall.IsChecked ?? false;
                _currentSettings.ListenHost = TxtListenHost.Text ?? "localhost";
                _currentSettings.AllowExternalAccess = false;
            }
            _currentSettings.ListenPort = (uint)(NumListenPort.Value ?? 1027);
            _currentSettings.DownloadThreadCount = (int)SliderDownloadThreadCount.Value;

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

    private void UpdateExternalAccessText(bool isChecked)
    {
        TxtExternalAccessStatus.Text = isChecked ? "已开启" : "已关闭";
    }

    private void SetLoadingState(bool isLoading)
    {
        _isLoading = isLoading;

        LoadingBar.IsVisible = isLoading;
        BtnSave.IsEnabled = !isLoading;
        BtnRefresh.IsEnabled = !isLoading;
        SwitchExternalAccess.IsEnabled = !isLoading;
        TxtListenHost.IsEnabled = !isLoading && !PlatformHelper.IsMacAppBundle();
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

    private void OnThemeColorChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_isUiLoaded || ComboThemeColor.SelectedItem is not ComboBoxItem item) return;
        var tag = item.Tag?.ToString();
        if(tag!= "Default")
        {
            SukiColorTheme theme = tag switch
            {
                "Teal" => new SukiColorTheme("Teal", Color.Parse("#20B2AA"), Color.Parse("#FF69B4")),
                "Purple" => new SukiColorTheme("Purple", Color.Parse("#8A2BE2"), Color.Parse("#00FF7F")),
                "Pink" => new SukiColorTheme("Pink", Color.Parse("#FFB6C1"), Color.Parse("#4682B4")),
                "Orange" => new SukiColorTheme("Orange", Color.Parse("#FF8C00"), Color.Parse("#4169E1")),
                "Red" => new SukiColorTheme("Red", Color.Parse("#DC143C"), Color.Parse("#00CED1")),
                "DarkCyan" => new SukiColorTheme("DarkCyan", Color.Parse("#008B8B"), Color.Parse("#FFD700")),
                _ => new SukiColorTheme("Blue", Color.Parse("#1E90FF"), Color.Parse("#FFA500")) // Blue
            };

            SukiTheme.GetInstance().ChangeColorTheme(theme);
        }
        else
        {
            SukiTheme.GetInstance().ChangeBaseTheme(Application.Current!.RequestedThemeVariant!);
        }
        
        ConfigService.Config.WriteConfigKey("ThemeColor", tag);
    }

    private void OnThemeModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_isUiLoaded || ComboThemeMode.SelectedItem is not ComboBoxItem item) return;
        var tag = item.Tag?.ToString();
        
        if (tag == "Light")
            Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        else if (tag == "Dark")
            Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;
        else
            Application.Current!.RequestedThemeVariant = ThemeVariant.Default;

        ConfigService.Config.WriteConfigKey("ThemeMode", tag);
    }

    private async void OnSelectBackgroundImage(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is MainWindow window)
        {
            var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择背景图片",
                AllowMultiple = false,
                FileTypeFilter = new[] { FilePickerFileTypes.ImageAll }
            });

            if (files != null && files.Count > 0)
            {
                var path = files[0].Path.LocalPath;
                ConfigService.Config.WriteConfigKey(GetConfigKey("BackgroundImage"), path);
                window.RefreshBackground();
            }
        }
    }

    private void OnClearBackgroundImage(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is MainWindow window)
        {
            ConfigService.Config.WriteConfigKey(GetConfigKey("BackgroundImage"), "");
            window.RefreshBackground();
        }
    }

    private void OnBackgroundOpacityChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_isUiLoaded) return;
        var opacity = e.NewValue;
        ConfigService.Config.WriteConfigKey(GetConfigKey("BackgroundOpacity"), opacity);
        
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is MainWindow window)
        {
            window.RefreshBackground();
        }
    }

    private void OnBackgroundBlurChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_isUiLoaded) return;
        var blur = e.NewValue;
        ConfigService.Config.WriteConfigKey(GetConfigKey("BackgroundBlur"), blur);
        
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is MainWindow window)
        {
            window.RefreshBackground();
        }
    }
}
