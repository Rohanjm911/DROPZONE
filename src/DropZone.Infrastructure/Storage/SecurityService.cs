using System.Security.Cryptography;
using DropZone.Domain.Interfaces;

namespace DropZone.Infrastructure.Storage;

public sealed class SecurityService : ISecurityService
{
    private static readonly HashSet<string> SuspiciousExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".sh", ".vbs", ".ps1", ".apk", ".deb", ".msi", ".scr", ".pif", ".hta", ".cpl", ".jar"
    };

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
        var ext = Path.GetExtension(filename);
        return !string.IsNullOrEmpty(ext) && SuspiciousExtensions.Contains(ext);
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
}
