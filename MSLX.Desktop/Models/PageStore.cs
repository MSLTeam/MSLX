using Avalonia.Controls;
using Material.Icons;
using Material.Icons.Avalonia;
using MSLX.Desktop.Views;
using MSLX.Desktop.Views.LinkDaemon;
using SukiUI.Controls;
using System.Collections.ObjectModel;

namespace MSLX.Desktop.Models
{
    internal class PageStore
    {
        public static ObservableCollection<SukiSideMenuItem> MainPages { get; set; } = new ObservableCollection<SukiSideMenuItem>
        {
            new SukiSideMenuItem
            {
                Header = "欢迎",
                Icon = new MaterialIcon()
                {
                    Kind = MaterialIconKind.HandWave,
                },
                PageContent = new WelcomePage(),
            },
            new() {
                Header = "主页",
                Icon = new MaterialIcon()
                {
                    Kind = MaterialIconKind.Home,
                },
                PageContent = new HomePage(),
            },
            new() {
                Header = "服务器列表",
                Icon = new MaterialIcon()
                {
                    Kind = MaterialIconKind.ViewList,
                },
                PageContent = new InstanceListPage(),
            },
            new() {
                Header = "内网映射",
                Icon = new MaterialIcon()
                {
                    Kind = MaterialIconKind.NavigationVariant,
                },
                PageContent = new TextBlock{Text="Hello World!"},
            },
            new() {
                Header = "点对点联机",
                Icon = new MaterialIcon()
                {
                    Kind = MaterialIconKind.SwapHorizontalBold,
                },
                // PageContent = new TextBlock{Text="Hello World!"},
            },
            new() {
                Header = "设置",
                Icon = new MaterialIcon()
                {
                    Kind = MaterialIconKind.Settings,
                },
                // PageContent = new TextBlock{Text="Hello World!"},
            },
            new() {
                Header = "关于",
                Icon = new MaterialIcon()
                {
                    Kind = MaterialIconKind.Info,
                },
                PageContent =new TextBlock{Text="Hello World!"},
            }
        };
    }
}
