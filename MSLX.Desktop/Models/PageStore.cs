using Material.Icons;
using Material.Icons.Avalonia;
using MSLX.Desktop.Views;
using MSLX.Desktop.Views.CreateInstance;
using MSLX.Desktop.Views.CreateTunnel.MSLFrp;
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
                PageContent = new TunnelListPage(),
            },
            new() {
                Header = "点对点联机",
                Icon = new MaterialIcon()
                {
                    Kind = MaterialIconKind.SwapHorizontalBold,
                },
                PageContent = new PlayerConnectionPage(),
            },
            new() {
                Header = "设置",
                Icon = new MaterialIcon()
                {
                    Kind = MaterialIconKind.Settings,
                },
                PageContent = new SettingsPage(),
            },
            new() {
                Header = "关于",
                Icon = new MaterialIcon()
                {
                    Kind = MaterialIconKind.Info,
                },
                PageContent =new AboutPage(),
            }
        };

        private static readonly CreateMCServer CreateMCServerPage = new();
        public static SukiSideMenuItem CreateMCServerMenuItem = new SukiSideMenuItem
        {
            Header = "创建实例",
            Icon = new MaterialIcon()
            {
                Kind = MaterialIconKind.AddCircle,
            },
            IsContentMovable = false,
            PageContent = PageStore.CreateMCServerPage
        };
        private static readonly CreateMSLFrpTunnel CreateMSLFrpTunnelPage = new();
        public static SukiSideMenuItem CreateMSLFrpTunnelMenuItem = new SukiSideMenuItem
        {
            Header = "添加隧道",
            Icon = new MaterialIcon()
            {
                Kind = MaterialIconKind.AddCircle,
            },
            IsContentMovable = false,
            PageContent = PageStore.CreateMSLFrpTunnelPage
        };
    }
}
