using Avalonia.Controls;
using MSLX.Desktop.Models;
using MSLX.Desktop.Utils;
using SukiUI.Controls;

namespace MSLX.Desktop;

public partial class MainWindow : SukiWindow
{
    public MainWindow()
    {
        InitializeComponent();

        this.Closing += MainWindow_Closing;

        this.DialogManager.Manager = DialogService.DialogManager;
        this.ToastManager.Manager = DialogService.ToastManager;
        SideMenuHelper.MainSideMenuHelper = new SideMenuHelper();
        SideMenuHelper.MainSideMenuHelper.SideMenu = this.MainSideMenu;
        this.MainSideMenu.ItemsSource = PageStore.MainPages;
        SideMenuHelper.MainSideMenuHelper?.HideMainPages(0);
    }

    private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        _ = DaemonManager.StopRunningDaemon();
    }
}