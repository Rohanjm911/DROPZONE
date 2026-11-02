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
        var appBase = AppDomain.CurrentDomain.BaseDirectory;
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var myDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var temp = Path.GetTempPath();

        return directoryType switch
        {
            StorageDirectoryType.Downloads => EnsureWritableDirectory(
                string.IsNullOrEmpty(userProfile) ? null : Path.Combine(userProfile, "Downloads", "DropZone"),
                string.IsNullOrEmpty(myDocs) ? null : Path.Combine(myDocs, "DropZone", "Downloads"),
                Path.Combine(appBase, "Downloads"),
                Path.Combine(temp, "DropZone", "Downloads")
            ),

            StorageDirectoryType.AppData => EnsureWritableDirectory(
                string.IsNullOrEmpty(localAppData) ? null : Path.Combine(localAppData, "DropZone"),
                string.IsNullOrEmpty(roamingAppData) ? null : Path.Combine(roamingAppData, "DropZone"),
                Path.Combine(appBase, "Data"),
                Path.Combine(temp, "DropZone", "Data")
            ),

            StorageDirectoryType.Cache => EnsureWritableDirectory(
                string.IsNullOrEmpty(localAppData) ? null : Path.Combine(localAppData, "DropZone", "Cache"),
                Path.Combine(appBase, "Cache"),
                Path.Combine(temp, "DropZone", "Cache")
            ),

            StorageDirectoryType.Quarantine => EnsureWritableDirectory(
                string.IsNullOrEmpty(localAppData) ? null : Path.Combine(localAppData, "DropZone", "Quarantine"),
                Path.Combine(appBase, "Quarantine"),
                Path.Combine(temp, "DropZone", "Quarantine")
            ),

            StorageDirectoryType.Temporary or _ => EnsureWritableDirectory(
                Path.Combine(temp, "DropZone"),
                Path.Combine(appBase, "Temp")
            )
        };
    }

    private static string EnsureWritableDirectory(params string?[] candidatePaths)
    {
        foreach (var path in candidatePaths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;

            try
            {
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }

                // Verify write capability with a transient probe
                var testFile = Path.Combine(path, $".dz_probe_{Guid.NewGuid():N}.tmp");
                File.WriteAllText(testFile, "probe");
                File.Delete(testFile);

                return path;
            }
            catch
            {
                // Access denied or read-only: try next candidate fallback
                continue;
            }
        }

        // Ultimate fallback: application base directory
        var fallback = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DropZoneData");
        try
        {
            Directory.CreateDirectory(fallback);
        }
        catch
        {
            // Ignore
        }
        return fallback;
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

