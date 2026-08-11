using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using MSLX.Desktop.Views.NetSparkle;
using NetSparkleUpdater;
using NetSparkleUpdater.Interfaces;
using SukiUI.Toasts;
using System;
using System.Collections.Generic;

namespace MSLX.Desktop.Utils;

/// <summary>
/// 使用 MSLX 主题承载 NetSparkle 更新流程，保留 NetSparkle 的下载和签名验证能力。
/// </summary>
internal sealed class MacAppUpdateUIFactory : IUIFactory
{
    private readonly WindowIcon? _icon;

    public MacAppUpdateUIFactory(WindowIcon? icon)
    {
        _icon = icon;
    }

    public bool HideReleaseNotes { get; set; }
    public bool HideSkipButton { get; set; }
    public bool HideRemindMeLaterButton { get; set; }
    public string? ReleaseNotesHTMLTemplate { get; set; }
    public string? AdditionalReleaseNotesHeaderHTML { get; set; }

    public IUpdateAvailable CreateUpdateAvailableWindow(
        List<AppCastItem> updates,
        ISignatureVerifier? signatureVerifier,
        string currentVersion = "",
        string appName = "MSLX",
        bool isUpdateAlreadyDownloaded = false)
    {
        var window = new UpdateAvailableWindow(updates, currentVersion, appName, isUpdateAlreadyDownloaded, _icon);
        if (HideReleaseNotes)
        {
            window.HideReleaseNotes();
        }

        if (HideSkipButton)
        {
            window.HideSkipButton();
        }

        if (HideRemindMeLaterButton)
        {
            window.HideRemindMeLaterButton();
        }

        return window;
    }

    public IDownloadProgress CreateProgressWindow(string downloadTitle, string actionButtonTitleAfterDownload)
    {
        return new DownloadProgressWindow(
            "正在下载 MSLX 更新",
            "安装并重新启动",
            _icon);
    }

    public ICheckingForUpdates ShowCheckingForUpdates()
    {
        return new CheckingForUpdatesWindow(_icon);
    }

    public void ShowUnknownInstallerFormatMessage(string downloadFileName)
    {
        ShowToast(NotificationType.Error, "更新失败", $"无法识别更新文件格式：{downloadFileName}");
    }

    public void ShowVersionIsUpToDate()
    {
        ShowToast(NotificationType.Success, "已是最新版本", "当前 MSLX 已经是最新版本。");
    }

    public void ShowVersionIsSkippedByUserRequest()
    {
        ShowToast(NotificationType.Information, "已跳过此版本", "你可以在下一次发布新版本时继续更新。");
    }

    public void ShowCannotDownloadAppcast(string? appcastUrl)
    {
        ShowToast(NotificationType.Error, "检查更新失败", "暂时无法获取更新信息，请检查网络连接后重试。");
    }

    public bool CanShowToastMessages() => true;

    public void ShowToast(Action clickHandler)
    {
        Dispatcher.UIThread.Post(() =>
        {
            DialogService.ToastManager.CreateToast()
                .OfType(NotificationType.Information)
                .WithTitle("发现新版本")
                .WithContent("有新的 MSLX 版本可用，点击查看更新内容。")
                .WithActionButton("查看更新", _ => clickHandler(), true)
                .Dismiss().After(TimeSpan.FromSeconds(12))
                .Queue();
        });
    }

    public void ShowDownloadErrorMessage(string message, string? appcastUrl)
    {
        ShowToast(NotificationType.Error, "下载更新失败", message);
    }

    public void Shutdown()
    {
        App.Instance?.ExitApplication();
    }

    private static void ShowToast(NotificationType type, string title, string content)
    {
        Dispatcher.UIThread.Post(() =>
        {
            DialogService.ToastManager.CreateToast()
                .OfType(type)
                .WithTitle(title)
                .WithContent(content)
                .Dismiss().After(TimeSpan.FromSeconds(5))
                .Queue();
        });
    }

}
