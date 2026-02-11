using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

namespace MSLX.Desktop.Models;

public partial class MCServerModel : ObservableObject
{
    public static MCServerModel Instance => _instance ??= new MCServerModel();
    private static MCServerModel? _instance;
    [ObservableProperty]
    private ObservableCollection<ServerInfo> _serverList = new();

    public class ServerInfo
    {
        public int ID { get; set; }
        public required string Name { get; set; }
        public required string Base { get; set; }
        public required string Java { get; set; }
        public required string Core { get; set; }
        public required int Status { get; set; } = 0;
        public required string StatusStr { get; set; }
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
// 本地Java信息模型
public class LocalJavaListModel
{
    [JsonProperty("path")]
    public string Path { get; set; } = "";
    [JsonProperty("home")]
    public string Home { get; set; } = "";
    [JsonProperty("version")]
    public string Version { get; set; } = "";
    [JsonProperty("vendor")]
    public string Vendor { get; set; } = "";
    [JsonProperty("is64Bit")]
    public bool Is64Bit { get; set; }
}
