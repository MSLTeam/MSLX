using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace MSLX.Desktop.Controls;

public partial class MagicMarkdown : UserControl
{
    public MagicMarkdown()
    {
        InitializeComponent();
    }

    public void ChangeMarkdownContent(string text)
    {
        MdViewer.Markdown = text;
    }
}