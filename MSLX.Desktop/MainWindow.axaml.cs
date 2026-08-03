using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using System.Linq;
using MSLX.Desktop.Models;
using MSLX.Desktop.Utils;
using SukiUI;
using SukiUI.Controls;
using SukiUI.Enums;
using SukiUI.Models;

namespace MSLX.Desktop;

public partial class MainWindow : SukiWindow
{
    public MainWindow()
    {
        InitializeComponent();

        ApplyVisualSettings();

        this.Closing += MainWindow_Closing;

        this.DialogManager.Manager = DialogService.DialogManager;
        this.ToastManager.Manager = DialogService.ToastManager;
        SideMenuHelper.Current = new SideMenuHelper();
        SideMenuHelper.Current.SideMenu = this.MainSideMenu;
        this.MainSideMenu.ItemsSource = PageStore.MainPages;
        SideMenuHelper.Current?.HideMainPages(0);
    }

    private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        _ = DaemonManager.StopRunningDaemon();
    }

    private void ApplyVisualSettings()
    {
        var themeMode = ConfigService.Config.ReadConfigKey("ThemeMode")?.ToString() ?? "System";
        if (themeMode == "Light")
            Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        else if (themeMode == "Dark")
            Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

        var themeColorStr = ConfigService.Config.ReadConfigKey("ThemeColor")?.ToString() ?? "Blue";
        SukiColorTheme theme = themeColorStr switch
        {
            "Teal" => new SukiColorTheme("Teal", Color.Parse("#20B2AA"), Color.Parse("#FF69B4")),
            "Purple" => new SukiColorTheme("Purple", Color.Parse("#8A2BE2"), Color.Parse("#00FF7F")),
            "Pink" => new SukiColorTheme("Pink", Color.Parse("#FFB6C1"), Color.Parse("#4682B4")),
            "Orange" => new SukiColorTheme("Orange", Color.Parse("#FF8C00"), Color.Parse("#4169E1")),
            "Red" => new SukiColorTheme("Red", Color.Parse("#DC143C"), Color.Parse("#00CED1")),
            "DarkCyan" => new SukiColorTheme("DarkCyan", Color.Parse("#008B8B"), Color.Parse("#FFD700")),
            _ => new SukiColorTheme("Blue", Color.Parse("#1E90FF"), Color.Parse("#FFA500"))
        };
        SukiTheme.GetInstance().ChangeColorTheme(theme);

        this.Loaded += (s, e) =>
        {
            var bgPath = ConfigService.Config.ReadConfigKey("BackgroundImage")?.ToString();
            SetBackgroundImage(bgPath);
        };
    }

    public void SetBackgroundImage(string? path)
    {
        var mainPanel = this.GetVisualDescendants().OfType<SukiMainHost>().FirstOrDefault();
        if (mainPanel == null) return;

        var rootPanel = mainPanel.GetVisualDescendants().OfType<Panel>().FirstOrDefault(p => p.Name == "PART_Root");
        if (rootPanel == null) return;

        var bgImage = rootPanel.Children.OfType<Image>().FirstOrDefault(i => i.Name == "CustomGlobalBg");

        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
        {
            if (bgImage != null)
                bgImage.Source = null;
            return;
        }

        try
        {
            var bitmap = new Avalonia.Media.Imaging.Bitmap(path);
            if (bgImage == null)
            {
                var opacityStr = ConfigService.Config.ReadConfigKey("BackgroundOpacity")?.ToString();
                var opacity = double.TryParse(opacityStr, out var parsed) ? parsed : 0.8;

                bgImage = new Image 
                { 
                    Name = "CustomGlobalBg", 
                    Stretch = Stretch.UniformToFill,
                    Opacity = opacity
                };
                // 背景图高于 SukiBackground
                rootPanel.Children.Insert(1, bgImage);
            }
            bgImage.Source = bitmap;
        }
        catch (Exception)
        {
            if (bgImage != null) bgImage.Source = null;
        }
    }

    public void SetBackgroundOpacity(double opacity)
    {
        var mainPanel = this.GetVisualDescendants().OfType<SukiMainHost>().FirstOrDefault();
        if (mainPanel == null) return;

        var rootPanel = mainPanel.GetVisualDescendants().OfType<Panel>().FirstOrDefault(p => p.Name == "PART_Root");
        if (rootPanel == null) return;

        var bgImage = rootPanel.Children.OfType<Image>().FirstOrDefault(i => i.Name == "CustomGlobalBg");
        if (bgImage != null)
        {
            bgImage.Opacity = opacity;
        }
    }
}