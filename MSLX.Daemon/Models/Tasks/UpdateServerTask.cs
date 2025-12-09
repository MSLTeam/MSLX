using MSLX.Daemon.Models.Instance;

namespace MSLX.Daemon.Models.Tasks;

public class UpdateServerTask
{
    public string ServerId => Request.ID.ToString();
    public UpdateServerRequest Request { get; set; } 
}