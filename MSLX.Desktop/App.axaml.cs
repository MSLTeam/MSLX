using AtomUI.Controls;
using AtomUI.Theme;
using AtomUI.Theme.Language;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace MSLX.Desktop
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
            this.UseAtomUI(builder =>
            {
                builder.WithDefaultLanguageVariant(LanguageVariant.zh_CN);
                builder.WithDefaultTheme(IThemeManager.DEFAULT_THEME_ID);
                builder.UseOSSControls();
                //builder.UseGalleryControls();
                //builder.UseOSSDataGrid();
                //builder.UseColorPicker();
            });
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}