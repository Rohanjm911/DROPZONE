using DropZone.Domain.Interfaces;

namespace DropZone.iOS;

public sealed class IOSPlatformService : IPlatformService
{
    private readonly string _baseStoragePath;
    private readonly string _deviceModel;
    private readonly string _osVersion;

    public PlatformType Platform => PlatformType.IOS;
    public bool IsMobile => true;
    public string DeviceModel => _deviceModel;
    public string OperatingSystemVersion => _osVersion;

    public IOSPlatformService(
        string? baseStoragePath = null,
        string? deviceModel = null,
        string? osVersion = null)
    {
        _baseStoragePath = baseStoragePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DropZone");
        _deviceModel = deviceModel ?? "Apple iPhone";
        _osVersion = osVersion ?? "iOS 18.0";

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
            StorageDirectoryType.Temporary => Path.Combine(Path.GetTempPath(), "DropZone"),
            _ => Path.Combine(Path.GetTempPath(), "DropZone")
        };

        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }

        return path;
    }

    public Task<bool> CheckPermissionAsync(PlatformPermission permission, CancellationToken ct = default)
    {
        // iOS permissions require NSLocalNetworkUsageDescription and runtime authorization
        return Task.FromResult(true);
    }

    public Task<bool> RequestPermissionAsync(PlatformPermission permission, CancellationToken ct = default)
    {
        return Task.FromResult(true);
    }
}

