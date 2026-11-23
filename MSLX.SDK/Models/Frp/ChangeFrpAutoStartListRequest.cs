using System.ComponentModel.DataAnnotations;

namespace MSLX.SDK.Models.Frp;

public class UpdateFrpAutoStartRequest
{
    [EnsureEightDigitIds(ErrorMessage = "列表包含非法的 ID，所有 ID 必须为 8 位数字")]
    public List<int>? FrpIds { get; init; }

    public List<string>? Tags { get; init; }
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public class EnsureEightDigitIdsAttribute : ValidationAttribute
{
    public override bool IsValid(object? value)
    {
        if (value is not IEnumerable<int> list) return true;
        return list.All(id => id is >= 10_000_000 and <= 99_999_999);
    }
}

