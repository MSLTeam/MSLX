using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using static MSLX.Desktop.Models.MSLFrpModel;

namespace MSLX.Desktop.ViewModels.CreateTunnel.MSLFrp
{
    public partial class MainPageViewModel : ViewModelBase
    {
        [ObservableProperty]
        private ObservableCollection<Tunnel> _tunnels = new();
        [ObservableProperty]
        private ObservableCollection<Node> _nodes = new();

        [ObservableProperty]
        private Tunnel? _selectedTunnel;
        [ObservableProperty]
        private Node? _selectedNode;


    }
}
