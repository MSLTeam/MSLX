namespace MSLX.Daemon.Models;

public class FrpInfoResponse
{
    public bool IsRunning { get; set; }
    public List<ProxyDetail> Proxies { get; set; } = new();
}

public class ProxyDetail
{
    public string ProxyName { get; set; }
    public string Type { get; set; }
    public string LocalAddress { get; set; }      
    public string RemoteAddressMain { get; set; }
    public string RemoteAddressBackup { get; set; }
}