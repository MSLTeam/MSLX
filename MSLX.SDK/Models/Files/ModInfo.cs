namespace MSLX.SDK.Models.Files;

public class ModInfo
{
    public string FileName { get; set; } = "";
    public string ModId { get; set; } = "";
    public bool IsClientOnly { get; set; }
    public HashSet<string> Dependencies { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
