using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace MSLX.Desktop.Models
{
    public class MCServerModel
    {
        public ObservableCollection<ServerInfo> ServerList { get; set; } = new ObservableCollection<ServerInfo>();
        public class ServerInfo
        {
            public int ID { get; set; }
            public required string Name { get; set; }
            public required string Base { get; set; }
            public required string Java { get; set; }
            public required string Core { get; set; }
            public bool IsRunning { get; set; } = false;
            public string Status => IsRunning ? "运行中" : "已停止";
            public int? MinM { get; set; }
            public int? MaxM { get; set; }
            public string? Args { get; set; }

            /*
            public ServerInfo(int _id, string _name, string _base, string _java, string _core, int _minM, int _maxM, string _args)
            {
                ID = _id;
                Name = _name;
                Base = _base;
                Java = _java;
                Core = _core;
                MinM = _minM;
                MaxM = _maxM;
                Args = _args;
            }
            */
        }
    }
}
