using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MSLX.Desktop.Utils;
using System;

namespace MSLX.Desktop
{
    public partial class App : Application
    {
        public static App? Instance { get; private set; }

        private IClassicDesktopStyleApplicationLifetime? _desktopLifetime;
        private bool _isExiting;

        public bool IsExitRequested => _isExiting;
        public bool IsMacAppBundle { get; private set; }

        public App()
        {
            Instance = this;
        }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);

            IsMacAppBundle = PlatformHelper.IsMacAppBundle();
            var trayIcons = TrayIcon.GetIcons(this);
            if (IsMacAppBundle && trayIcons != null)
            {
                foreach (var trayIcon in trayIcons)
                {
                    trayIcon.IsVisible = true;
                    MacOSProperties.SetIsTemplateIcon(trayIcon, true);
                }
            }
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                _desktopLifetime = desktop;

                // macOS 关闭最后一个窗口后仍保持进程运行，等待菜单栏或 Dock 激活。
                if (IsMacAppBundle)
                {
                    desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                }

                if (IsMacAppBundle)
                {
                    // Dock 右键“退出”、系统退出等请求必须允许真正关闭应用。
                    desktop.ShutdownRequested += (_, _) => _isExiting = true;
                }
                desktop.MainWindow = new MainWindow();
            }

            if (IsMacAppBundle && Application.Current?.TryGetFeature<IActivatableLifetime>() is { } activatable)
            {
                activatable.Activated += Application_Activated;
            }

            base.OnFrameworkInitializationCompleted();
        }

        public void ShowMainWindow()
        {
            if (_desktopLifetime?.MainWindow is not MainWindow window)
            {
                return;
            }

            Dispatcher.UIThread.Post(() =>
            {
                Application.Current?.TryGetFeature<IActivatableLifetime>()?.TryLeaveBackground();

                if (window.WindowState == WindowState.Minimized)
                {
                    window.WindowState = WindowState.Normal;
                }

                window.Show();
                window.Activate();
                window.Focus();
            });
        }

        public void ExitApplication()
        {
            _isExiting = true;
            _desktopLifetime?.Shutdown();
        }

        private void Application_Activated(object? sender, ActivatedEventArgs e)
        {
            // macOS 用户点击 Dock 图标或再次打开 .app 时会触发 Reopen。
            if (e.Kind == ActivationKind.Reopen)
            {
                ShowMainWindow();
            }
        }

        private void TrayOpen_Click(object? sender, EventArgs e)
        {
            ShowMainWindow();
        }

        private void TrayExit_Click(object? sender, EventArgs e)
        {
            ExitApplication();
        }
    }
}
