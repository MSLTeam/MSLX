using CommunityToolkit.Mvvm.ComponentModel;
using MSLX.Desktop.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using static MSLX.Desktop.Models.MSLFrpModel;

namespace MSLX.Desktop.ViewModels
{
    public partial class TunnelListPageViewModel : ViewModelBase
    {
        [ObservableProperty]
        private ObservableCollection<TunnelModel> _tunnels = new();
    }
}
