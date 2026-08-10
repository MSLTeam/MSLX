using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NetSparkleUpdater;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.Events;
using NetSparkleUpdater.Interfaces;
using SukiUI.Controls;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MSLX.Desktop.Views.NetSparkle;

internal partial class UpdateAvailableWindow : SukiWindow, IUpdateAvailable
{
    private readonly List<AppCastItem> _updates;
    private bool _hasResponded;
    public UpdateAvailableWindow(
        List<AppCastItem> updates,
        string currentVersion,
        string appName,
        bool isUpdateAlreadyDownloaded,
        WindowIcon? icon)
    {
        InitializeComponent();
        _updates = updates;
        Icon = icon;

        AppCastItem item = CurrentItem;
        string availableVersion = item.ShortVersion ?? item.Version ?? "未知版本";
        string installedVersion = string.IsNullOrWhiteSpace(currentVersion) ? "未知版本" : currentVersion;

        CurrentVersionText.Text = installedVersion;
        AvailableVersionText.Text = availableVersion;
        CriticalBadge.IsVisible = item.IsCriticalUpdate;
        PublicationDateText.Text = item.PublicationDate == DateTime.MinValue
            ? string.Empty
            : item.PublicationDate.ToLocalTime().ToString("yyyy年M月d日");
        ReleaseNotesText.Text = string.IsNullOrWhiteSpace(item.Description)
            ? "此版本暂无更新说明。"
            : item.Description.Trim();
        InstallButtonText.Text = isUpdateAlreadyDownloaded ? "立即安装" : "下载并安装";

        Closed += OnWindowClosed;
    }

    public event UserRespondedToUpdate? UserResponded;

    public UpdateAvailableResult Result { get; private set; } = UpdateAvailableResult.None;

    public AppCastItem CurrentItem => _updates.First();

    public new void Show()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsVisible)
            {
                Window? owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
                if (owner != null)
                {
                    Show(owner);
                }
                else
                {
                    base.Show();
                }
            }

            Activate();
        });
    }

    public void HideReleaseNotes()
    {
        ReleaseNotesSection.IsVisible = false;
    }

    public void HideRemindMeLaterButton() => RemindButton.IsVisible = false;

    public void HideSkipButton() => SkipButton.IsVisible = false;

    public void BringToFront()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (IsVisible)
            {
                Activate();
                Topmost = true;
                Topmost = false;
            }
        });
    }

    public new void Close()
    {
        Dispatcher.UIThread.Post(base.Close);
    }

    private void SkipButton_Click(object? sender, RoutedEventArgs e) => Respond(UpdateAvailableResult.SkipUpdate);

    private void RemindButton_Click(object? sender, RoutedEventArgs e) => Respond(UpdateAvailableResult.RemindMeLater);

    private void InstallButton_Click(object? sender, RoutedEventArgs e) => Respond(UpdateAvailableResult.InstallUpdate);

    private void Respond(UpdateAvailableResult result)
    {
        if (_hasResponded)
        {
            return;
        }

        _hasResponded = true;
        Result = result;
        UserResponded?.Invoke(this, new UpdateResponseEventArgs(result, CurrentItem));
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (!_hasResponded)
        {
            Respond(UpdateAvailableResult.RemindMeLater);
        }
    }

}
