namespace DropZone.Domain.Models;

public enum SyncConflictResolution
{
    SourceWins,
    TargetWins,
    NewerTimestampWins,
    LargerFileWins,
    ManualReview
}

public enum SyncItemStatus
{
    Added,
    Modified,
    Deleted,
    Identical,
    Conflict
}

public sealed class SyncFileItem
{
    public string RelativePath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime LastModifiedUtc { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}

public sealed class SyncDifference
{
    public string RelativePath { get; set; } = string.Empty;
    public SyncItemStatus Status { get; set; }
    public SyncFileItem? SourceFile { get; set; }
    public SyncFileItem? TargetFile { get; set; }
    public string ActionRequired { get; set; } = string.Empty;
}

public sealed class FolderSyncManifest
{
    public Guid SyncJobId { get; set; } = Guid.NewGuid();
    public string SourceFolder { get; set; } = string.Empty;
    public string TargetFolder { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<SyncFileItem> SourceFiles { get; set; } = new();
    public List<SyncFileItem> TargetFiles { get; set; } = new();
    public List<SyncDifference> Differences { get; set; } = new();

    public int TotalChanges => Differences.Count(d => d.Status != SyncItemStatus.Identical);
    public long TotalTransferBytes => Differences
        .Where(d => d.Status is SyncItemStatus.Added or SyncItemStatus.Modified)
        .Sum(d => d.SourceFile?.SizeBytes ?? 0);
}

public sealed class FolderSyncJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
    public string RemoteDeviceId { get; set; } = string.Empty;
    public string RemoteDeviceName { get; set; } = string.Empty;
    public string RemotePath { get; set; } = string.Empty;
    public SyncConflictResolution ConflictPolicy { get; set; } = SyncConflictResolution.NewerTimestampWins;
    public bool AutoSyncOnChanges { get; set; } = true;
    public DateTime? LastSyncUtc { get; set; }
    public bool IsSyncing { get; set; }
    public string? LastError { get; set; }
}
