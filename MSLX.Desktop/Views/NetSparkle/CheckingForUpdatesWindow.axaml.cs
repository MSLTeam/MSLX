using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using NetSparkleUpdater.Interfaces;
using SukiUI.Controls;
using System;

namespace MSLX.Desktop.Views.NetSparkle;

internal partial class CheckingForUpdatesWindow : SukiWindow, ICheckingForUpdates
{
    private bool _closed;

    public CheckingForUpdatesWindow(WindowIcon? icon)
    {
        InitializeComponent();
        Icon = icon;
        Closed += (_, _) =>
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            UpdatesUIClosing?.Invoke(this, EventArgs.Empty);
        };
    }

    public event EventHandler? UpdatesUIClosing;

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
        });
    }

    public new void Close()
    {
        Dispatcher.UIThread.Post(base.Close);
    }
}
