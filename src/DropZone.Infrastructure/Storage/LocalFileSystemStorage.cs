using DropZone.Domain.Interfaces;
using DropZone.Domain.Models;

namespace DropZone.Infrastructure.Storage;

public sealed class LocalFileSystemStorage : IFileSystemStorage
{
    private readonly string _downloadRoot;
    private readonly ISecurityService _security;

    public LocalFileSystemStorage(string downloadRoot, ISecurityService security)
    {
        _downloadRoot = Path.GetFullPath(downloadRoot);
        _security = security;
        Directory.CreateDirectory(_downloadRoot);
    }

    public string GetDownloadPath(string relativePath)
    {
        if (!_security.IsPathSafe(relativePath, out var safePath))
            throw new InvalidOperationException($"Dangerous relative path detected: {relativePath}");

        return Path.Combine(_downloadRoot, safePath);
    }

    private string GetPartFilePath(Guid jobId, string relativePath)
    {
        var targetPath = GetDownloadPath(relativePath);
        return $"{targetPath}.{jobId:N}.dropzone_part";
    }

    public async Task<TransferManifest> BuildManifestAsync(
        string sourcePath, 
        string senderDeviceId, 
        string senderDeviceName, 
        CancellationToken ct = default)
    {
        var manifest = new TransferManifest
        {
            JobId = Guid.NewGuid(),
            SenderDeviceId = senderDeviceId,
            SenderDeviceName = senderDeviceName,
            Files = new List<FileItem>()
        };

        if (File.Exists(sourcePath))
        {
            var fi = new FileInfo(sourcePath);
            var sha = await _security.ComputeFileSha256Async(fi.FullName, ct);
            manifest.Files.Add(new FileItem
            {
                Index = 0,
                RelativePath = fi.Name,
                SizeBytes = fi.Length,
                Sha256 = sha
            });
            manifest.TotalBytes = fi.Length;
        }
        else if (Directory.Exists(sourcePath))
        {
            var di = new DirectoryInfo(sourcePath);
            var allFiles = di.GetFiles("*", SearchOption.AllDirectories);
            long total = 0;
            int idx = 0;

            foreach (var file in allFiles)
            {
                ct.ThrowIfCancellationRequested();
                var rel = Path.GetRelativePath(sourcePath, file.FullName);
                var sha = await _security.ComputeFileSha256Async(file.FullName, ct);
                manifest.Files.Add(new FileItem
                {
                    Index = idx++,
                    RelativePath = rel,
                    SizeBytes = file.Length,
                    Sha256 = sha
                });
                total += file.Length;
            }
            manifest.TotalBytes = total;
        }
        else
        {
            throw new FileNotFoundException($"Source path does not exist: {sourcePath}");
        }

        return manifest;
    }

    public Task<long> GetExistingResumeOffsetAsync(
        Guid jobId, 
        string relativePath, 
        long expectedTotalSize, 
        string expectedSha256, 
        CancellationToken ct = default)
    {
        var partFile = GetPartFilePath(jobId, relativePath);
        if (!File.Exists(partFile))
            return Task.FromResult(0L);

        var fi = new FileInfo(partFile);
        if (fi.Length > expectedTotalSize)
        {
            // Part file exceeds expected size, corrupt, restart
            File.Delete(partFile);
            return Task.FromResult(0L);
        }

        // Align to 64KB boundary for chunk consistency
        var alignedOffset = (fi.Length / 65536) * 65536;
        return Task.FromResult(alignedOffset);
    }

    public async Task WriteChunkAsync(
        Guid jobId, 
        string relativePath, 
        long offset, 
        byte[] data, 
        CancellationToken ct = default)
    {
        var partFile = GetPartFilePath(jobId, relativePath);
        var dir = Path.GetDirectoryName(partFile);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Open random access stream
        await using var stream = new FileStream(
            partFile, 
            FileMode.OpenOrCreate, 
            FileAccess.Write, 
            FileShare.None, 
            bufferSize: 65536, 
            useAsync: true);

        stream.Seek(offset, SeekOrigin.Begin);
        await stream.WriteAsync(data, ct);
    }

    public async Task<bool> FinalizeFileAsync(
        Guid jobId, 
        string relativePath, 
        string expectedSha256, 
        CancellationToken ct = default)
    {
        var partFile = GetPartFilePath(jobId, relativePath);
        var targetFile = GetDownloadPath(relativePath);

        if (!File.Exists(partFile))
            return false;

        // Verify SHA-256
        var actualSha = await _security.ComputeFileSha256Async(partFile, ct);
        if (!string.Equals(actualSha, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            // Integrity failure! Quarantine corrupt part file
            var quarantinePath = $"{targetFile}.{jobId:N}.corrupt";
            if (File.Exists(quarantinePath)) File.Delete(quarantinePath);
            File.Move(partFile, quarantinePath);
            return false;
        }

        var dir = Path.GetDirectoryName(targetFile);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        if (File.Exists(targetFile))
        {
            File.Delete(targetFile);
        }

        File.Move(partFile, targetFile);
        return true;
    }

    public Task CleanPartFilesAsync(Guid jobId, CancellationToken ct = default)
    {
        try
        {
            var pattern = $"*.{jobId:N}.dropzone_part";
            var files = Directory.GetFiles(_downloadRoot, pattern, SearchOption.AllDirectories);
            foreach (var f in files)
            {
                File.Delete(f);
            }
        }
        catch
        {
            // Best effort cleanup
        }
        return Task.CompletedTask;
    }
}

