using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NetSparkleUpdater.Events;
using NetSparkleUpdater.Interfaces;
using SukiUI.Controls;
using System;

namespace MSLX.Desktop.Views.NetSparkle;

internal partial class DownloadProgressWindow : SukiWindow, IDownloadProgress
{
    private bool _downloadFinished;
    private bool _completionRaised;

    public DownloadProgressWindow(string downloadTitle, string actionButtonTitleAfterDownload, WindowIcon? icon)
    {
        InitializeComponent();
        Icon = icon;
        DownloadTitleText.Text = downloadTitle;
        ActionButtonText.Text = "取消下载";
        _actionButtonTitleAfterDownload = actionButtonTitleAfterDownload;
        DownloadStatusText.Text = "准备下载...";
        DownloadPercentageText.Text = "0%";
        Closed += OnWindowClosed;
    }

    private readonly string _actionButtonTitleAfterDownload;

    public event DownloadInstallEventHandler? DownloadProcessCompleted;

    public void SetDownloadAndInstallButtonEnabled(bool shouldBeEnabled)
    {
        Dispatcher.UIThread.Post(() => ActionButton.IsEnabled = shouldBeEnabled);
    }

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

    public void OnDownloadProgressChanged(object? sender, ItemDownloadProgressEventArgs args)
    {
        Dispatcher.UIThread.Post(() =>
        {
            DownloadProgressBar.Value = args.ProgressPercentage;
            DownloadPercentageText.Text = $"{args.ProgressPercentage}%";
            DownloadStatusText.Text = FormatProgress(args.BytesReceived, args.TotalBytesToReceive);
        });
    }

    public new void Close()
    {
        Dispatcher.UIThread.Post(base.Close);
    }

    public void FinishedDownloadingFile(bool isDownloadedFileValid)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _downloadFinished = isDownloadedFileValid;
            DownloadProgressBar.Value = isDownloadedFileValid ? 100 : 0;
            DownloadPercentageText.Text = isDownloadedFileValid ? "100%" : string.Empty;
            DownloadStatusText.Text = isDownloadedFileValid ? "下载完成，文件验证通过" : "下载失败或文件验证未通过";
            ActionButtonText.Text = isDownloadedFileValid ? _actionButtonTitleAfterDownload : "关闭";
            ErrorBorder.IsVisible = !isDownloadedFileValid;
            if (!isDownloadedFileValid)
            {
                ErrorText.Text = "更新文件无法通过验证，请稍后重试。";
            }
        });
    }

    public bool DisplayErrorMessage(string errorMessage)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ErrorBorder.IsVisible = true;
            ErrorText.Text = errorMessage;
        });
        return true;
    }

    private void ActionButton_Click(object? sender, RoutedEventArgs e)
    {
        RaiseCompletion(_downloadFinished);
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (!_completionRaised)
        {
            RaiseCompletion(false);
        }
    }

    private void RaiseCompletion(bool shouldInstall)
    {
        if (_completionRaised)
        {
            return;
        }

        _completionRaised = true;
        DownloadProcessCompleted?.Invoke(this, new DownloadInstallEventArgs(shouldInstall));
    }

    private static string FormatProgress(long bytesReceived, long totalBytes)
    {
        if (totalBytes <= 0)
        {
            return $"已下载 {FormatBytes(bytesReceived)}";
        }

        return $"已下载 {FormatBytes(bytesReceived)} / {FormatBytes(totalBytes)}";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024 * 1024)
        {
            return $"{bytes / 1024d / 1024d:0.0} MB";
        }

        return $"{Math.Max(0, bytes) / 1024d:0} KB";
    }
}
