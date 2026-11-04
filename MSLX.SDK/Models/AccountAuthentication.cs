namespace MSLX.SDK.Models;

public sealed class AccountAuthentication
{
    public string? ProtectedTotpSecret { get; set; }
    public long LastTotpStep { get; set; } = -1;
    public List<string> RecoveryCodeHashes { get; set; } = [];
    public List<AccountPasskey> Passkeys { get; set; } = [];
}

public sealed class AccountPasskey
{
    public string Id { get; set; } = "";
    public string PublicKey { get; set; } = "";
    public string RpId { get; set; } = "";
    public string Name { get; set; } = "";
    public uint SignCount { get; set; }
    public bool IsBackupEligible { get; set; }
    public bool IsBackedUp { get; set; }
    public string[] Transports { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}

public sealed class AccountSecuritySummary
{
    public bool TotpEnabled { get; set; }
    public int RecoveryCodesRemaining { get; set; }
    public List<PasskeySummary> Passkeys { get; set; } = [];
}

public sealed record PasskeySummary(string Id, string Name, string RpId, DateTime CreatedAt, DateTime? LastUsedAt);
