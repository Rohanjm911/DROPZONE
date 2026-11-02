using DropZone.Domain.Enums;

namespace DropZone.Domain.Interfaces;

public enum PlatformType
{
    Windows,
    Linux,
    MacOS,
    Android,
    IOS,
    Unknown
}

public enum StorageDirectoryType
{
    Downloads,
    AppData,
    Cache,
    Quarantine,
    Temporary
}

public enum PlatformPermission
{
    Storage,
    Notifications,
    LocalNetwork,
    WakeLock
}

public enum AppLifecycleState
{
    Foreground,
    Background,
    Suspended,
    Terminating
}

public interface IPlatformService
{
    PlatformType Platform { get; }
    bool IsMobile { get; }
    string DeviceModel { get; }
    string OperatingSystemVersion { get; }
    string GetStorageDirectory(StorageDirectoryType directoryType);
    Task<bool> CheckPermissionAsync(PlatformPermission permission, CancellationToken ct = default);
    Task<bool> RequestPermissionAsync(PlatformPermission permission, CancellationToken ct = default);
}

public interface IPlatformNotificationService
{
    Task ShowTransferProgressNotificationAsync(
        string jobId,
        string fileName,
        int progressPercentage,
        double speedBps,
        int etaSeconds,
        bool isOngoing = true,
        CancellationToken ct = default);

    Task ShowTransferCompletedNotificationAsync(
        string jobId,
        string fileName,
        long totalBytes,
        bool isSuccess,
        string? errorMessage = null,
        CancellationToken ct = default);

    Task ShowSecurityAlertNotificationAsync(
        string title,
        string message,
        string? details = null,
        CancellationToken ct = default);

    Task CancelNotificationAsync(string jobId, CancellationToken ct = default);
}

public interface IPlatformWakeLockService
{
    Task<bool> AcquireWakeLockAsync(string lockId, string reason, CancellationToken ct = default);
    Task<bool> ReleaseWakeLockAsync(string lockId, CancellationToken ct = default);
    bool IsLockActive(string lockId);
    IReadOnlyCollection<string> ActiveLocks { get; }
}

public interface IPlatformStoragePicker
{
    Task<IReadOnlyList<string>> PickFilesAsync(string title, bool allowMultiple = true, CancellationToken ct = default);
    Task<string?> PickFolderAsync(string title, CancellationToken ct = default);
    Task<string?> PickSaveFileAsync(string title, string defaultFileName, string? extension = null, CancellationToken ct = default);
}

public interface IPlatformLifecycleService
{
    AppLifecycleState CurrentState { get; }
    event EventHandler<AppLifecycleState>? LifecycleStateChanged;
    Task NotifyBackgroundTransferStartingAsync(string jobId, CancellationToken ct = default);
    Task NotifyBackgroundTransferFinishedAsync(string jobId, CancellationToken ct = default);
}
