namespace DropZone.Domain.Models;

public enum AuditEventType
{
    DeviceEnrolled,
    DeviceRevoked,
    DeviceBlocked,
    TransferStarted,
    TransferCompleted,
    TransferRejected,
    TransferCancelled,
    TransferFailed,
    SecurityAlertTriggered,
    FileQuarantined,
    PolicyUpdated,
    VaultBackupCreated,
    VaultBackupRestored
}

public sealed class ComplianceAuditEntry
{
    public long SequenceNumber { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public AuditEventType EventType { get; set; }
    public string ActorDeviceId { get; set; } = string.Empty;
    public string TargetDeviceId { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string PreviousEntryHash { get; set; } = string.Empty;
    public string EntryHash { get; set; } = string.Empty;
}

public sealed class EnterpriseCertificate
{
    public string Subject { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Thumbprint { get; set; } = string.Empty;
    public string PublicKeyBase64 { get; set; } = string.Empty;
    public DateTime ValidFromUtc { get; set; }
    public DateTime ValidToUtc { get; set; }
    public bool IsRevoked { get; set; }
    public List<string> AllowedRoles { get; set; } = new();

    public bool IsCurrentlyValid => !IsRevoked && DateTime.UtcNow >= ValidFromUtc && DateTime.UtcNow <= ValidToUtc;
}

public sealed class EnterprisePolicy
{
    public bool EnforceCertificatesOnly { get; set; }
    public bool DisallowUnmanagedDevices { get; set; }
    public long MaxAllowedFileSize { get; set; } = 50L * 1024 * 1024 * 1024; // 50 GB
    public long GlobalBandwidthLimitBps { get; set; } = 0; // 0 = unlimited
    public bool RequireAuditChainVerificationOnStartup { get; set; } = true;
    public List<string> TrustedRootThumbprints { get; set; } = new();
}

public sealed class AuditChainVerificationResult
{
    public bool IsValid { get; set; }
    public long TotalEntriesVerified { get; set; }
    public long? BrokenSequenceNumber { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}
