using Newtonsoft.Json;

namespace MSLX.Daemon.Models.Frp;

public class FrpDownloadApiResponse
{
    public int Code { get; set; }
    public FrpDownloadData Data { get; set; }
}

public class FrpDownloadData
{
    public List<FrpVersionInfo> Cli { get; set; }
}

public class FrpVersionInfo
{
    public string Version { get; set; }
    public Dictionary<string, Dictionary<string, FrpArtifact>> Download { get; set; }
}

public class FrpArtifact
{
    public string Url { get; set; }
    public string Sha256 { get; set; }
}