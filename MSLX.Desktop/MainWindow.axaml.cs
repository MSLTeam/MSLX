using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using System.Linq;
using MSLX.Desktop.Models;
using MSLX.Desktop.Utils;
using MSLX.Desktop.Views;
using SukiUI;
using SukiUI.Controls;
using SukiUI.Models;

namespace MSLX.Desktop;

public partial class MainWindow : SukiWindow
{
    public static MainWindow? Instance { get; private set; }

    public MainWindow()
    {
        InitializeComponent();
        Instance = this;

        ApplyVisualSettings();

        this.Closing += MainWindow_Closing;

        this.DialogManager.Manager = DialogService.DialogManager;
        this.ToastManager.Manager = DialogService.ToastManager;
        SideMenuHelper.Current = new SideMenuHelper();
        SideMenuHelper.Current.SideMenu = this.MainSideMenu;
        this.MainSideMenu.ItemsSource = PageStore.MainPages;
        SideMenuHelper.Current?.HideMainPages(0);
    }

    private void NativeMenuAbout_Click(object? sender, EventArgs e)
    {
        App.Instance?.ShowMainWindow();
        SideMenuHelper.Current?.NavigateTo<AboutPage>();
    }

    protected override void OnApplyTemplate(Avalonia.Controls.Primitives.TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        // 让Dialog和Toast下移，避免遮挡标题栏
        // 先找到标题栏控件，然后监听其Bounds属性变化
        var titleBar = e.NameScope.Find<Control>("PART_TitleBar");
        titleBar?.PropertyChanged += (s, ev) =>
            {
                if (ev.Property == BoundsProperty)
                {
                    // 获取标题栏高度
                    var h = titleBar.Bounds.Height;
                    if (h > 0)
                    {
                        // 调整DialogManager和ToastManager的Margin
                        DialogManager?.Margin = new Avalonia.Thickness(0, h, 0, 0);
                        ToastManager?.Margin = new Avalonia.Thickness(0, h + 3, 0, 0);
                    }
                }
            };
    }

    public void UpdateUserHeader(string username, string? avatarUrl)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!string.IsNullOrEmpty(username))
            {
                MenuUsernameText.Text = username;
            }

            if (!string.IsNullOrWhiteSpace(avatarUrl))
            {
                _ = LoadMenuAvatarAsync(avatarUrl);
            }
        });
    }

    private async System.Threading.Tasks.Task LoadMenuAvatarAsync(string avatarUrl)
    {
        try
        {
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var data = await client.GetByteArrayAsync(avatarUrl);
            using var stream = new System.IO.MemoryStream(data);
            var bitmap = new Avalonia.Media.Imaging.Bitmap(stream);

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                MenuLogoImage.IsVisible = false;
                MenuAvatarEllipse.Fill = new Avalonia.Media.ImageBrush(bitmap) { Stretch = Avalonia.Media.Stretch.UniformToFill };
                MenuAvatarEllipse.IsVisible = true;
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载菜单头像失败: {ex.Message}");
        }
    }

    private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        // macOS 下关闭窗口只隐藏应用，Daemon 和菜单栏图标继续运行。
        if ((App.Instance?.IsMacAppBundle ?? false) &&
            !(App.Instance?.IsExitRequested ?? false))
        {
            e.Cancel = true;
            Hide();
            return;
        }

        _ = DaemonManager.StopRunningDaemon();
    }

    private void ApplyVisualSettings()
    {
        var themeMode = ConfigService.Config.ReadConfigKey("ThemeMode")?.ToString() ?? "System";
        if (themeMode == "Light")
            Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        else if (themeMode == "Dark")
            Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Dark;

        var themeColorStr = ConfigService.Config.ReadConfigKey("ThemeColor")?.ToString() ?? "Default";
        if (themeColorStr != "Default")
        {
            SukiColorTheme theme = themeColorStr switch
            {
                "Teal" => new SukiColorTheme("Teal", Color.Parse("#20B2AA"), Color.Parse("#FF69B4")),
                "Purple" => new SukiColorTheme("Purple", Color.Parse("#8A2BE2"), Color.Parse("#00FF7F")),
                "Pink" => new SukiColorTheme("Pink", Color.Parse("#FFB6C1"), Color.Parse("#4682B4")),
                "Orange" => new SukiColorTheme("Orange", Color.Parse("#FF8C00"), Color.Parse("#4169E1")),
                "Red" => new SukiColorTheme("Red", Color.Parse("#DC143C"), Color.Parse("#00CED1")),
                "DarkCyan" => new SukiColorTheme("DarkCyan", Color.Parse("#008B8B"), Color.Parse("#FFD700")),
                _ => new SukiColorTheme("Blue", Color.Parse("#1E90FF"), Color.Parse("#FFA500")), // Blue
            };
            SukiTheme.GetInstance().ChangeColorTheme(theme);
        }

        this.Loaded += (s, e) =>
        {
            RefreshBackground();

#if DEBUG
            // 开启 FPS 监控悬浮窗
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel != null)
            {
                topLevel.RendererDiagnostics.DebugOverlays = Avalonia.Rendering.RendererDebugOverlays.Fps;
            }
#endif
        };

        Avalonia.Application.Current?.ActualThemeVariantChanged += (s, e) =>
        {
            RefreshBackground();
        };
    }

    public void RefreshBackground()
    {
        bool isDark = Avalonia.Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
        string themeSuffix = isDark ? "_Dark" : "_Light";

        var bgPath = ConfigService.Config.ReadConfigKey($"BackgroundImage{themeSuffix}")?.ToString();
        if (string.IsNullOrEmpty(bgPath)) bgPath = ConfigService.Config.ReadConfigKey("BackgroundImage")?.ToString();

        var opacityStr = ConfigService.Config.ReadConfigKey($"BackgroundOpacity{themeSuffix}")?.ToString();
        if (string.IsNullOrEmpty(opacityStr)) opacityStr = ConfigService.Config.ReadConfigKey("BackgroundOpacity")?.ToString();
        var opacity = double.TryParse(opacityStr, out var parsedOpacity) ? parsedOpacity : 0.8;

        var blurStr = ConfigService.Config.ReadConfigKey($"BackgroundBlur{themeSuffix}")?.ToString();
        if (string.IsNullOrEmpty(blurStr)) blurStr = ConfigService.Config.ReadConfigKey("BackgroundBlur")?.ToString();
        var blur = double.TryParse(blurStr, out var parsedBlur) ? parsedBlur : 0;

        Control? mainPanel = (Control?)this.GetVisualDescendants().OfType<SukiMainPanel>().FirstOrDefault()
                             ?? this.GetVisualDescendants().OfType<SukiMainHost>().FirstOrDefault();
        if (mainPanel == null) return;

        var rootPanel = mainPanel.GetVisualDescendants().OfType<Panel>().FirstOrDefault(p => p.Name == "PART_Root");
        if (rootPanel == null) return;

        var bgImage = rootPanel.Children.OfType<Image>().FirstOrDefault(i => i.Name == "CustomGlobalBg");

        if (string.IsNullOrEmpty(bgPath) || !System.IO.File.Exists(bgPath))
        {
            if (bgImage != null)
                bgImage.Source = null;
            return;
        }

        try
        {
            var bitmap = new Avalonia.Media.Imaging.Bitmap(bgPath);

            if (bgImage == null)
            {
                bgImage = new Image 
                { 
                    Name = "CustomGlobalBg", 
                    Stretch = Stretch.UniformToFill,
                    Opacity = opacity,
                    Effect = blur > 0 ? new Avalonia.Media.BlurEffect { Radius = blur } : null
                };
                // 背景图高于 SukiBackground
                rootPanel.Children.Insert(1, bgImage);
            }
            else
            {
                bgImage.Opacity = opacity;
                bgImage.Effect = blur > 0 ? new Avalonia.Media.BlurEffect { Radius = blur } : null;
            }
            bgImage.Source = bitmap;
        }
        catch (Exception)
        {
            if (bgImage != null) bgImage.Source = null;
        }
    }
}
