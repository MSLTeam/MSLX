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

        public int SelectedTunnelIndex
        {
            get => _selectedTunnelIndex;
            set
            {
                if (SetProperty(ref _selectedTunnelIndex, value))
                {
                    OnPropertyChanged(nameof(SelectedTunnelIndex));
                }
                if (value != -1)
                {
                    SelectedTunnel = Tunnels[value];
                }
            }
        }
        private int _selectedTunnelIndex = -1;

        public int SelectedNodeIndex
        {
            get => _selectedNodeIndex;
            set
            {
                if (SetProperty(ref _selectedNodeIndex, value))
                {
                    OnPropertyChanged(nameof(SelectedNodeIndex));
                }
                if (value != -1)
                {
                    SelectedNode = Nodes[value];
                }
            }
        }
        private int _selectedNodeIndex = -1;

    }
}
