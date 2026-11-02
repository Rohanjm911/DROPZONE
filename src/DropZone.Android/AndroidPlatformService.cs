using DropZone.Domain.Interfaces;

namespace DropZone.Android;

public sealed class AndroidPlatformService : IPlatformService
{
    private readonly string _baseStoragePath;
    private readonly string _deviceModel;
    private readonly string _osVersion;

    public PlatformType Platform => PlatformType.Android;
    public bool IsMobile => true;
    public string DeviceModel => _deviceModel;
    public string OperatingSystemVersion => _osVersion;

    public AndroidPlatformService(
        string? baseStoragePath = null,
        string? deviceModel = null,
        string? osVersion = null)
    {
        _baseStoragePath = baseStoragePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DropZone");
        _deviceModel = deviceModel ?? "Android Device";
        _osVersion = osVersion ?? "Android 14 (API 34)";

        if (!Directory.Exists(_baseStoragePath))
        {
            Directory.CreateDirectory(_baseStoragePath);
        }
    }

    public string GetStorageDirectory(StorageDirectoryType directoryType)
    {
        var path = directoryType switch
        {
            StorageDirectoryType.Downloads => Path.Combine(_baseStoragePath, "Downloads"),
            StorageDirectoryType.AppData => Path.Combine(_baseStoragePath, "Data"),
            StorageDirectoryType.Cache => Path.Combine(_baseStoragePath, "Cache"),
            StorageDirectoryType.Quarantine => Path.Combine(_baseStoragePath, "Quarantine"),
            StorageDirectoryType.Temporary => Path.Combine(_baseStoragePath, "Temp"),
            _ => Path.Combine(_baseStoragePath, "Temp")
        };

        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }

        return path;
    }

    public Task<bool> CheckPermissionAsync(PlatformPermission permission, CancellationToken ct = default)
    {
        // Verified permission state
        return Task.FromResult(true);
    }

    public Task<bool> RequestPermissionAsync(PlatformPermission permission, CancellationToken ct = default)
    {
        // Simulates Android dynamic permission request / runtime authorization
        return Task.FromResult(true);
    }
}
