using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MSLX.Daemon.Models.Instance;


public class WhitelistItem
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public class OpItem
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("level")]
    public int Level { get; set; } = 4; // 默认给最高权限 4

    [JsonPropertyName("bypassesPlayerLimit")]
    public bool BypassesPlayerLimit { get; set; } = false; // 默认不绕过人数限制
}

public class BannedIpItem
{
    [JsonPropertyName("ip")]
    public string Ip { get; set; } = string.Empty;

    [JsonPropertyName("created")]
    public string Created { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = "Server"; // 默认 Server

    [JsonPropertyName("expires")]
    public string Expires { get; set; } = "forever"; // 默认永久

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "Banned by an operator."; // 默认理由
}

public class BannedPlayerItem
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("created")]
    public string Created { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = "Server";

    [JsonPropertyName("expires")]
    public string Expires { get; set; } = "forever";

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "Banned by an operator.";
}

public class UserCacheItem
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = string.Empty;

    [JsonPropertyName("expiresOn")]
    public string ExpiresOn { get; set; } = string.Empty;
}

// ==================== 请求 DTO 校验模型 ====================

public class AddPlayerRequest
{
    [Required(ErrorMessage = "玩家名称不能为空")]
    [StringLength(16, MinimumLength = 1, ErrorMessage = "玩家名称长度必须在1到16之间")]
    public string Name { get; set; } = string.Empty;

    public string? Uuid { get; set; } // 可选，不填则去 cache 查
}

public class RemovePlayerRequest
{
    [Required(ErrorMessage = "玩家名称不能为空")]
    public string Name { get; set; } = string.Empty;
}

public class AddBannedIpRequest
{
    [Required(ErrorMessage = "IP不能为空")]
    [RegularExpression(@"^([0-9]{1,3}\.){3}[0-9]{1,3}$", ErrorMessage = "IP格式不正确")]
    public string Ip { get; set; } = string.Empty;

    public string? Reason { get; set; }
}

public class RemoveBannedIpRequest
{
    [Required(ErrorMessage = "IP不能为空")]
    public string Ip { get; set; } = string.Empty;
}

public class AddBannedPlayerRequest
{
    [Required(ErrorMessage = "玩家名称不能为空")]
    public string Name { get; set; } = string.Empty;

    public string? Uuid { get; set; }
    public string? Reason { get; set; }
}