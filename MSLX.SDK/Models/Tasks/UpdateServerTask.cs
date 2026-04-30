using MSLX.SDK.Models.Instance;

namespace MSLX.SDK.Models.Tasks;

public class UpdateServerTask
{
    public string ServerId => Request.ID.ToString();
    public UpdateServerRequest Request { get; set; } 
}