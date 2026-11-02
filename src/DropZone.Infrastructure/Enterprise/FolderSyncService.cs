using System.Security.Cryptography;
using DropZone.Domain.Interfaces;
using DropZone.Domain.Models;
using Microsoft.Extensions.Logging;

namespace DropZone.Infrastructure.Enterprise;

public sealed class FolderSyncService : IFolderSyncService
{
    private readonly ILogger<FolderSyncService>? _logger;

    public FolderSyncService(ILogger<FolderSyncService>? logger = null)
    {
        _logger = logger;
    }

    public async Task<List<SyncFileItem>> ScanDirectoryAsync(string directoryPath, CancellationToken ct = default)
    {
        var items = new List<SyncFileItem>();
        if (!Directory.Exists(directoryPath))
            return items;

        var dirInfo = new DirectoryInfo(directoryPath);
        var files = dirInfo.GetFiles("*", SearchOption.AllDirectories);

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var relPath = Path.GetRelativePath(directoryPath, file.FullName).Replace('\\', '/');

            // Compute SHA-256
            string hash;
            await using (var stream = file.OpenRead())
            {
                var hashBytes = await SHA256.HashDataAsync(stream, ct);
                hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
            }

            items.Add(new SyncFileItem
            {
                RelativePath = relPath,
                SizeBytes = file.Length,
                LastModifiedUtc = file.LastWriteTimeUtc,
                Sha256 = hash
            });
        }

        return items;
    }

    public async Task<FolderSyncManifest> ComputeDeltaAsync(
        string sourceFolder,
        string targetFolder,
        SyncConflictResolution policy,
        CancellationToken ct = default)
    {
        var sourceFiles = await ScanDirectoryAsync(sourceFolder, ct);
        var targetFiles = await ScanDirectoryAsync(targetFolder, ct);

        var manifest = new FolderSyncManifest
        {
            SourceFolder = sourceFolder,
            TargetFolder = targetFolder,
            SourceFiles = sourceFiles,
            TargetFiles = targetFiles
        };

        var sourceMap = sourceFiles.ToDictionary(f => f.RelativePath, StringComparer.OrdinalIgnoreCase);
        var targetMap = targetFiles.ToDictionary(f => f.RelativePath, StringComparer.OrdinalIgnoreCase);

        // Check source files vs target
        foreach (var src in sourceFiles)
        {
            if (!targetMap.TryGetValue(src.RelativePath, out var tgt))
            {
                manifest.Differences.Add(new SyncDifference
                {
                    RelativePath = src.RelativePath,
                    Status = SyncItemStatus.Added,
                    SourceFile = src,
                    TargetFile = null,
                    ActionRequired = "Copy to target"
                });
            }
            else
            {
                // Both exist - compare hashes
                if (string.Equals(src.Sha256, tgt.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    manifest.Differences.Add(new SyncDifference
                    {
                        RelativePath = src.RelativePath,
                        Status = SyncItemStatus.Identical,
                        SourceFile = src,
                        TargetFile = tgt,
                        ActionRequired = "None"
                    });
                }
                else
                {
                    // Conflict / Modification
                    var diff = new SyncDifference
                    {
                        RelativePath = src.RelativePath,
                        Status = SyncItemStatus.Modified,
                        SourceFile = src,
                        TargetFile = tgt
                    };

                    diff.ActionRequired = policy switch
                    {
                        SyncConflictResolution.SourceWins => "Overwrite target from source",
                        SyncConflictResolution.TargetWins => "Overwrite source from target",
                        SyncConflictResolution.NewerTimestampWins => src.LastModifiedUtc >= tgt.LastModifiedUtc
                            ? "Overwrite target from source (Source is newer)"
                            : "Overwrite source from target (Target is newer)",
                        SyncConflictResolution.LargerFileWins => src.SizeBytes >= tgt.SizeBytes
                            ? "Overwrite target from source (Source is larger)"
                            : "Overwrite source from target (Target is larger)",
                        SyncConflictResolution.ManualReview => "Conflict - requires manual review",
                        _ => "Overwrite target from source"
                    };

                    manifest.Differences.Add(diff);
                }
            }
        }

        // Check target files that don't exist in source
        foreach (var tgt in targetFiles)
        {
            if (!sourceMap.ContainsKey(tgt.RelativePath))
            {
                manifest.Differences.Add(new SyncDifference
                {
                    RelativePath = tgt.RelativePath,
                    Status = SyncItemStatus.Deleted,
                    SourceFile = null,
                    TargetFile = tgt,
                    ActionRequired = "Deleted in source"
                });
            }
        }

        _logger?.LogInformation("[FolderSync] Computed delta between '{Src}' and '{Tgt}': {Count} changes detected",
            sourceFolder, targetFolder, manifest.TotalChanges);

        return manifest;
    }

    public async Task<int> ExecuteSyncPlanAsync(FolderSyncManifest manifest, CancellationToken ct = default)
    {
        int executed = 0;

        foreach (var diff in manifest.Differences)
        {
            ct.ThrowIfCancellationRequested();

            if (diff.Status == SyncItemStatus.Identical)
                continue;

            var targetFilePath = Path.Combine(manifest.TargetFolder, diff.RelativePath.Replace('/', Path.DirectorySeparatorChar));

            if (diff.Status is SyncItemStatus.Added or SyncItemStatus.Modified)
            {
                if (diff.SourceFile != null)
                {
                    var sourceFilePath = Path.Combine(manifest.SourceFolder, diff.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                    var targetDir = Path.GetDirectoryName(targetFilePath);
                    if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }

                    // Atomic write via part file
                    var tempPart = targetFilePath + ".syncpart";
                    await using (var inStream = File.OpenRead(sourceFilePath))
                    await using (var outStream = File.Create(tempPart))
                    {
                        await inStream.CopyToAsync(outStream, ct);
                    }

                    File.Move(tempPart, targetFilePath, overwrite: true);
                    executed++;
                }
            }
        }

        return executed;
    }
}
