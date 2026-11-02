using DropZone.Domain.Models;

namespace DropZone.Domain.Interfaces;

public interface IFolderSyncService
{
    Task<List<SyncFileItem>> ScanDirectoryAsync(string directoryPath, CancellationToken ct = default);
    Task<FolderSyncManifest> ComputeDeltaAsync(string sourceFolder, string targetFolder, SyncConflictResolution policy, CancellationToken ct = default);
    Task<int> ExecuteSyncPlanAsync(FolderSyncManifest manifest, CancellationToken ct = default);
}

public interface IComplianceAuditService
{
    Task InitializeAsync(CancellationToken ct = default);
    Task<ComplianceAuditEntry> LogEventAsync(
        AuditEventType eventType,
        string actorDeviceId,
        string targetDeviceId,
        string details,
        CancellationToken ct = default);
    Task<AuditChainVerificationResult> VerifyAuditChainIntegrityAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ComplianceAuditEntry>> GetAuditLogAsync(int limit = 200, CancellationToken ct = default);
    Task<string> ExportAuditReportJsonAsync(CancellationToken ct = default);
}

public interface IEnterpriseSecurityService
{
    Task InitializeAsync(CancellationToken ct = default);
    Task<bool> ValidateDeviceCertificateAsync(string deviceId, EnterpriseCertificate certificate, CancellationToken ct = default);
    Task RegisterTrustedRootCertificateAsync(EnterpriseCertificate rootCertificate, CancellationToken ct = default);
    Task RevokeCertificateAsync(string thumbprint, string reason, CancellationToken ct = default);
    Task<EnterprisePolicy> GetPolicyAsync(CancellationToken ct = default);
    Task UpdatePolicyAsync(EnterprisePolicy policy, CancellationToken ct = default);
}

public interface IBandwidthThrottler
{
    long CurrentLimitBytesPerSecond { get; }
    void SetLimit(long bytesPerSecond);
    Task ThrottleAsync(int bytesToSend, CancellationToken ct = default);
}
