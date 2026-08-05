using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Material.Icons;

namespace MSLX.Desktop.Controls;

public partial class EmptyStateControl : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<EmptyStateControl, string>(nameof(Title), "暂无内容");

    public static readonly StyledProperty<string> DescriptionProperty =
        AvaloniaProperty.Register<EmptyStateControl, string>(nameof(Description), string.Empty);

    public static readonly StyledProperty<MaterialIconKind> IconKindProperty =
        AvaloniaProperty.Register<EmptyStateControl, MaterialIconKind>(nameof(IconKind), MaterialIconKind.InboxOutline);

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public MaterialIconKind IconKind
    {
        get => GetValue(IconKindProperty);
        set => SetValue(IconKindProperty, value);
    }

    public EmptyStateControl()
    {
        InitializeComponent();
        DataContext = this;
    }
}
