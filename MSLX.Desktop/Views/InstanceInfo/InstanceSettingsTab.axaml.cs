using Avalonia.Controls;
using MSLX.Desktop.Models;
using MSLX.Desktop.Services;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace MSLX.Desktop.Views.InstanceInfo;

public partial class InstanceSettingsTab : UserControl
{
    private int _instanceId;
    private InstanceModel.InstanceGeneralSettings? _original;
    public Action<string, bool>? OnSaveResult { get; set; } // 保存成功/失败回调，可由外部注入用于显示 Toast 等

    public InstanceSettingsTab()
    {
        InitializeComponent();
    }

    #region 公开API
    /// <summary>
    /// 从 API 加载设置并填充表单
    /// </summary>
    public async Task LoadAsync(int instanceId)
    {
        _instanceId = instanceId;

        var (success, settings, msg) = await InstanceService.GetGeneralSettingsAsync(instanceId);
        if (!success || settings == null)
        {
            OnSaveResult?.Invoke($"加载设置失败: {msg}", false);
            return;
        }

        _original = settings;
        FillForm(settings);
    }
    #endregion

    #region 事件
    private async void OnSaveClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SaveBtn.IsEnabled = false;
        try
        {
            var updated = BuildSettings();
            var (ok, msg) = await InstanceService.UpdateGeneralSettingsAsync(_instanceId, updated);
            OnSaveResult?.Invoke(ok ? "保存成功" : $"保存失败: {msg}", ok);

            if (ok)
                _original = updated;
        }
        finally
        {
            SaveBtn.IsEnabled = true;
        }
    }
    #endregion

    #region 表单辅助
    private void FillForm(InstanceModel.InstanceGeneralSettings s)
    {
        NameBox.Text         = s.Name;
        JavaBox.Text         = s.Java;
        CoreBox.Text         = s.Core;
        BaseBox.Text         = s.Base;
        MinMBox.Value        = s.MinM;
        MaxMBox.Value        = s.MaxM;
        ArgsBox.Text         = s.Args;
        YggdrasilBox.Text    = s.YggdrasilApiAddr;
        AutoRestartBox.IsChecked  = s.AutoRestart;
        RunOnStartupBox.IsChecked = s.RunOnStartup;
        BackupPathBox.Text        = s.BackupPath;
        BackupMaxCountBox.Value   = s.BackupMaxCount;
        BackupDelayBox.Value      = s.BackupDelay;
        SelectComboItem(InputEncodingBox, s.InputEncoding);
        SelectComboItem(OutputEncodingBox, s.OutputEncoding);
        AllowOriginASCIIColorsToggle.IsChecked = s.AllowOriginASCIIColors;
    }

    private InstanceModel.InstanceGeneralSettings BuildSettings() => new()
    {
        Id              = _instanceId,
        Name            = NameBox.Text ?? "",
        Java            = JavaBox.Text ?? "",
        Core            = CoreBox.Text ?? "",
        Base            = BaseBox.Text ?? "",
        MinM            = (int)(MinMBox.Value ?? 1024),
        MaxM            = (int)(MaxMBox.Value ?? 4096),
        Args            = ArgsBox.Text ?? "",
        YggdrasilApiAddr = YggdrasilBox.Text ?? "",
        AutoRestart     = AutoRestartBox.IsChecked ?? false,
        RunOnStartup    = RunOnStartupBox.IsChecked ?? false,
        BackupPath      = BackupPathBox.Text ?? "",
        BackupMaxCount  = (int)(BackupMaxCountBox.Value ?? 20),
        BackupDelay     = (int)(BackupDelayBox.Value ?? 10),
        InputEncoding   = GetSelectedComboText(InputEncodingBox),
        OutputEncoding  = GetSelectedComboText(OutputEncodingBox),
        AllowOriginASCIIColors = AllowOriginASCIIColorsToggle.IsChecked ?? true,
    };

    #endregion

    private static void SelectComboItem(ComboBox box, string value)
    {
        foreach (var item in box.Items.OfType<ComboBoxItem>())
        {
            if (item.Content?.ToString()?.Equals(value, StringComparison.OrdinalIgnoreCase) == true)
            {
                box.SelectedItem = item;
                return;
            }
        }
        box.SelectedIndex = 0; // 默认 utf-8
    }

    private static string GetSelectedComboText(ComboBox box)
        => (box.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "utf-8";
}
