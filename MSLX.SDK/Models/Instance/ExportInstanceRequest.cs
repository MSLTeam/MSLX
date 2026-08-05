using System.Collections.Generic;

namespace MSLX.SDK.Models.Instance;

public class ExportInstanceRequest
{
    public List<string> Excludes { get; set; } = new List<string>();
}
