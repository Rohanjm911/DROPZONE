using DropZone.Domain.Models;

namespace DropZone.Domain.Interfaces;

public sealed class EngineConfig
{
    public string DeviceId { get; set; } = Guid.NewGuid().ToString("N");
    public string DeviceName { get; set; } = Environment.MachineName;
    public string DownloadDirectory { get; set; } = string.Empty;
    public ushort ListenPort { get; set; } = 52490;
    public bool AutoAcceptTrusted { get; set; } = true;
}

public sealed class EngineEvent
{
    public int EventType { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
}

public interface IDropZoneEngine : IAsyncDisposable
{
    Task InitializeAsync(EngineConfig config, CancellationToken ct = default);
    Task StartServerAsync(string bindAddress, ushort port, CancellationToken ct = default);
    Task StopServerAsync(CancellationToken ct = default);
    Task StartDiscoveryAsync(string deviceName, ushort port, CancellationToken ct = default);
    Task StopDiscoveryAsync(CancellationToken ct = default);
    Task SendJobAsync(string peerIp, ushort peerPort, TransferManifest manifest, CancellationToken ct = default);
    Task AcceptJobAsync(Guid jobId, ulong resumeOffset, CancellationToken ct = default);
    Task RejectJobAsync(Guid jobId, string reason, CancellationToken ct = default);
    Task PauseJobAsync(Guid jobId, CancellationToken ct = default);
    Task ResumeJobAsync(Guid jobId, CancellationToken ct = default);
    Task CancelJobAsync(Guid jobId, CancellationToken ct = default);
    Task<IdentityKeyPair> GenerateIdentityKeyAsync(CancellationToken ct = default);
    Task StartPairingAsync(string peerIp, ushort peerPort, CancellationToken ct = default);
    Task<bool> ConfirmPairingAsync(string peerDeviceId, string pinCode, bool isAccepted, CancellationToken ct = default);
    Task<NatCandidate?> TestStunAsync(string stunServer, CancellationToken ct = default);
    Task GatherNatCandidatesAsync(string stunServer, CancellationToken ct = default);
    Task PingPeerAsync(string peerIp, ushort peerPort, CancellationToken ct = default);

    event EventHandler<EngineEvent>? EventReceived;
}

public interface ITransferRepository
{
    Task InitializeAsync(CancellationToken ct = default);
    Task AddJobAsync(TransferJob job, CancellationToken ct = default);
    Task UpdateJobStateAsync(Guid jobId, Enums.TransferState state, string? error = null, CancellationToken ct = default);
    Task UpdateJobProgressAsync(Guid jobId, long transferredBytes, double speedBps, int etaSec, CancellationToken ct = default);
    Task<TransferJob?> GetJobAsync(Guid jobId, CancellationToken ct = default);
    Task<IReadOnlyList<TransferJob>> GetAllJobsAsync(int limit = 100, CancellationToken ct = default);
    Task DeleteJobAsync(Guid jobId, CancellationToken ct = default);
}

public interface IDeviceRepository
{
    Task InitializeAsync(CancellationToken ct = default);
    Task UpsertDeviceAsync(Device device, CancellationToken ct = default);
    Task<Device?> GetDeviceAsync(string deviceId, CancellationToken ct = default);
    Task<IReadOnlyList<Device>> GetAllDevicesAsync(CancellationToken ct = default);
    Task UpdateTrustLevelAsync(string deviceId, Enums.DeviceTrustLevel trustLevel, CancellationToken ct = default);
    Task RevokeDeviceAsync(string deviceId, CancellationToken ct = default);
    Task BlockDeviceAsync(string deviceId, CancellationToken ct = default);
    Task<DevicePermissions> GetPermissionsAsync(string deviceId, CancellationToken ct = default);
    Task UpdatePermissionsAsync(DevicePermissions permissions, CancellationToken ct = default);
}

public interface IBackupService
{
    Task<byte[]> CreateEncryptedBackupAsync(string passphrase, CancellationToken ct = default);
    Task<DropZoneBackupPackage> RestoreEncryptedBackupAsync(byte[] backupBytes, string passphrase, CancellationToken ct = default);
    Task ExportBackupToFileAsync(string filePath, string passphrase, CancellationToken ct = default);
    Task<DropZoneBackupPackage> ImportBackupFromFileAsync(string filePath, string passphrase, CancellationToken ct = default);
}

public interface IFileSystemStorage
{
    Task<TransferManifest> BuildManifestAsync(string sourcePath, string senderDeviceId, string senderDeviceName, CancellationToken ct = default);
    Task<long> GetExistingResumeOffsetAsync(Guid jobId, string relativePath, long expectedTotalSize, string expectedSha256, CancellationToken ct = default);
    Task WriteChunkAsync(Guid jobId, string relativePath, long offset, byte[] data, CancellationToken ct = default);
    Task<bool> FinalizeFileAsync(Guid jobId, string relativePath, string expectedSha256, CancellationToken ct = default);
    Task CleanPartFilesAsync(Guid jobId, CancellationToken ct = default);
    string GetDownloadPath(string relativePath);
}

public interface ISecurityService
{
    bool IsPathSafe(string relativePath, out string sanitizedPath);
    bool IsSuspiciousFileType(string filename);
    SuspiciousFileAnalysis AnalyzeFile(string fileName, long fileSize = 0);
    string ComputeSha256(byte[] data);
    Task<string> ComputeFileSha256Async(string filePath, CancellationToken ct = default);
    Task<QuarantinedFileRecord> QuarantineFileAsync(string sourceFilePath, string relativePath, string reason, string sourceDeviceId, CancellationToken ct = default);
    Task<bool> RestoreQuarantinedFileAsync(Guid quarantineId, string destinationPath, CancellationToken ct = default);
    Task<IReadOnlyList<QuarantinedFileRecord>> GetQuarantinedFilesAsync(CancellationToken ct = default);
    Task<bool> DeleteQuarantinedFileAsync(Guid quarantineId, CancellationToken ct = default);
}

