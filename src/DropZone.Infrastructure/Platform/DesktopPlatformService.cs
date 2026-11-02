using System.Runtime.InteropServices;
using DropZone.Domain.Interfaces;

namespace DropZone.Infrastructure.Platform;

public sealed class DesktopPlatformService : IPlatformService
{
    public PlatformType Platform
    {
        get
        {
            if (OperatingSystem.IsWindows()) return PlatformType.Windows;
            if (OperatingSystem.IsMacOS()) return PlatformType.MacOS;
            if (OperatingSystem.IsLinux()) return PlatformType.Linux;
            return PlatformType.Unknown;
        }
    }

    public bool IsMobile => false;

    public string DeviceModel => Environment.MachineName;

    public string OperatingSystemVersion => Environment.OSVersion.ToString();

    public string GetStorageDirectory(StorageDirectoryType directoryType)
    {
        string path = directoryType switch
        {
            StorageDirectoryType.Downloads => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "DropZone"),
            StorageDirectoryType.AppData => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DropZone"),
            StorageDirectoryType.Cache => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DropZone", "Cache"),
            StorageDirectoryType.Quarantine => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DropZone", "Quarantine"),
            StorageDirectoryType.Temporary => Path.Combine(
                Path.GetTempPath(), "DropZone"),
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
        // Desktop OS accounts typically have standard local network, storage, and notification permissions
        return Task.FromResult(true);
    }

    public Task<bool> RequestPermissionAsync(PlatformPermission permission, CancellationToken ct = default)
    {
        return Task.FromResult(true);
    }
}

