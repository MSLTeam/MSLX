using MSLX.SDK.Models.Instance;

namespace MSLX.SDK.Models.Tasks;

public class UpdateServerTask
{
    public string ServerId => Request.ID.ToString();
    public string BackgroundTaskId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public UpdateServerRequest Request { get; set; } 
}