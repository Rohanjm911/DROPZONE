using System.Security.Cryptography;
using System.Text.Json;
using DropZone.Domain.Interfaces;
using DropZone.Domain.Models;

namespace DropZone.Infrastructure.Storage;

public sealed class SecurityService : ISecurityService
{
    private readonly string _quarantineRoot;

    private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".sh", ".vbs", ".ps1", ".apk", ".deb", ".msi", ".scr", ".pif", ".hta", ".cpl", ".jar", ".com", ".reg"
    };

    private static readonly HashSet<string> InnocentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".jpg", ".jpeg", ".png", ".gif", ".mp4", ".mp3", ".doc", ".docx", ".xls", ".xlsx", ".txt", ".zip", ".tar", ".gz"
    };

    public SecurityService(string? quarantineRoot = null)
    {
        _quarantineRoot = quarantineRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
            "DropZone", 
            "Quarantine");

        Directory.CreateDirectory(_quarantineRoot);
    }

    public bool IsPathSafe(string relativePath, out string sanitizedPath)
    {
        sanitizedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(relativePath))
            return false;

        // Disallow null bytes or path separators at start
        if (relativePath.Contains('\0') || relativePath.StartsWith('/') || relativePath.StartsWith('\\'))
            return false;

        // Disallow drive letters like C:
        if (relativePath.Length >= 2 && relativePath[1] == ':')
            return false;

        // Split and inspect components
        var parts = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var safeParts = new List<string>();

        foreach (var part in parts)
        {
            if (part == "." || string.IsNullOrWhiteSpace(part))
                continue;

            if (part == "..")
                return false; // Path traversal attempt

            // Check invalid characters
            if (part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return false;

            // Reject Windows reserved DOS names (with or without extension)
            var baseName = Path.GetFileNameWithoutExtension(part).ToUpperInvariant();
            if (baseName is "CON" or "PRN" or "AUX" or "NUL" or "COM1" or "COM2" or "COM3" or "COM4" or "LPT1" or "LPT2" or "LPT3")
                return false;

            safeParts.Add(part);
        }

        if (safeParts.Count == 0)
            return false;

        sanitizedPath = string.Join(Path.DirectorySeparatorChar, safeParts);
        return true;
    }

    public bool IsSuspiciousFileType(string filename)
    {
        return AnalyzeFile(filename).IsSuspicious;
    }

    public SuspiciousFileAnalysis AnalyzeFile(string fileName, long fileSize = 0)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return SuspiciousFileAnalysis.Clean(fileName);

        // 1. Check for Unicode Right-To-Left Override (RTLO) spoofing
        if (fileName.Contains('\u202E') || fileName.Contains('\u202D') || fileName.Contains('\u202B'))
        {
            return SuspiciousFileAnalysis.Flagged(
                fileName, 
                ThreatRiskLevel.Critical, 
                "Dangerous Unicode bidirectional override character (RTLO spoofing attempt)");
        }

        var ext = Path.GetExtension(fileName);
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        var secondaryExt = Path.GetExtension(fileNameWithoutExt);

        // 2. Check disguised double extensions (e.g. document.pdf.exe)
        if (!string.IsNullOrEmpty(secondaryExt) && InnocentExtensions.Contains(secondaryExt) && ExecutableExtensions.Contains(ext))
        {
            return SuspiciousFileAnalysis.Flagged(
                fileName, 
                ThreatRiskLevel.Critical, 
                $"Disguised executable masquerading as {secondaryExt} using double extension {ext}");
        }

        // 3. Check direct executable or script file extensions
        if (!string.IsNullOrEmpty(ext) && ExecutableExtensions.Contains(ext))
        {
            return SuspiciousFileAnalysis.Flagged(
                fileName, 
                ThreatRiskLevel.High, 
                $"Executable or script file type ({ext}) can execute arbitrary code on this device");
        }

        return SuspiciousFileAnalysis.Clean(fileName);
    }

    public string ComputeSha256(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexStringLower(hash);
    }

    public async Task<string> ComputeFileSha256Async(string filePath, CancellationToken ct = default)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, useAsync: true);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, ct);
        return Convert.ToHexStringLower(hash);
    }

    public async Task<QuarantinedFileRecord> QuarantineFileAsync(
        string sourceFilePath, 
        string relativePath, 
        string reason, 
        string sourceDeviceId, 
        CancellationToken ct = default)
    {
        if (!File.Exists(sourceFilePath))
            throw new FileNotFoundException($"Cannot quarantine nonexistent file: {sourceFilePath}");

        var id = Guid.NewGuid();
        var sha = await ComputeFileSha256Async(sourceFilePath, ct);
        var originalFileName = Path.GetFileName(sourceFilePath);
        var quarantinedPath = Path.Combine(_quarantineRoot, $"{id:N}.quarantine");
        var metaPath = Path.Combine(_quarantineRoot, $"{id:N}.meta.json");

        // Move to quarantine
        File.Move(sourceFilePath, quarantinedPath, overwrite: true);

        // Set file as read-only to prevent execution or modification
        try
        {
            File.SetAttributes(quarantinedPath, FileAttributes.ReadOnly);
        }
        catch
        {
            // Best effort across platforms
        }

        var record = new QuarantinedFileRecord
        {
            Id = id,
            OriginalFileName = originalFileName,
            OriginalRelativePath = relativePath,
            QuarantinedFilePath = quarantinedPath,
            Sha256 = sha,
            ThreatReason = reason,
            SourceDeviceId = sourceDeviceId,
            QuarantinedAt = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(metaPath, json, ct);

        return record;
    }

    public async Task<bool> RestoreQuarantinedFileAsync(Guid quarantineId, string destinationPath, CancellationToken ct = default)
    {
        var quarantinedPath = Path.Combine(_quarantineRoot, $"{quarantineId:N}.quarantine");
        var metaPath = Path.Combine(_quarantineRoot, $"{quarantineId:N}.meta.json");

        if (!File.Exists(quarantinedPath))
            return false;

        // Clear read-only attribute
        try
        {
            File.SetAttributes(quarantinedPath, FileAttributes.Normal);
        }
        catch
        {
            // Ignore
        }

        var destDir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        File.Move(quarantinedPath, destinationPath, overwrite: true);

        if (File.Exists(metaPath))
        {
            File.Delete(metaPath);
        }

        return await Task.FromResult(true);
    }

    public async Task<IReadOnlyList<QuarantinedFileRecord>> GetQuarantinedFilesAsync(CancellationToken ct = default)
    {
        var list = new List<QuarantinedFileRecord>();
        if (!Directory.Exists(_quarantineRoot))
            return list;

        var metaFiles = Directory.GetFiles(_quarantineRoot, "*.meta.json");
        foreach (var meta in metaFiles)
        {
            try
            {
                var json = await File.ReadAllTextAsync(meta, ct);
                var record = JsonSerializer.Deserialize<QuarantinedFileRecord>(json);
                if (record != null)
                {
                    list.Add(record);
                }
            }
            catch
            {
                // Ignore corrupt metadata
            }
        }

        return list;
    }

    public Task<bool> DeleteQuarantinedFileAsync(Guid quarantineId, CancellationToken ct = default)
    {
        var quarantinedPath = Path.Combine(_quarantineRoot, $"{quarantineId:N}.quarantine");
        var metaPath = Path.Combine(_quarantineRoot, $"{quarantineId:N}.meta.json");

        if (File.Exists(quarantinedPath))
        {
            try
            {
                File.SetAttributes(quarantinedPath, FileAttributes.Normal);
            }
            catch { }
            File.Delete(quarantinedPath);
        }

        if (File.Exists(metaPath))
        {
            File.Delete(metaPath);
        }

        return Task.FromResult(true);
    }
}
