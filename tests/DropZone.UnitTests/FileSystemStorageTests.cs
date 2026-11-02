using System.Text;
using DropZone.Infrastructure.Storage;
using Xunit;

namespace DropZone.UnitTests;

public class FileSystemStorageTests : IDisposable
{
    private readonly string _testDir;
    private readonly SecurityService _security;
    private readonly LocalFileSystemStorage _storage;

    public FileSystemStorageTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"dropzone_fs_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _security = new SecurityService();
        _storage = new LocalFileSystemStorage(_testDir, _security);
    }

    [Fact]
    public async Task BuildManifest_SingleFile_BuildsValidManifestWithSha256()
    {
        var filePath = Path.Combine(_testDir, "sample.txt");
        var content = "This is a DropZone file transfer payload verification test.";
        await File.WriteAllTextAsync(filePath, content);

        var manifest = await _storage.BuildManifestAsync(filePath, "dev-1", "Sender");

        Assert.NotNull(manifest);
        Assert.Single(manifest.Files);
        Assert.Equal("sample.txt", manifest.Files[0].RelativePath);
        Assert.Equal(content.Length, manifest.TotalBytes);
        Assert.Equal(64, manifest.Files[0].Sha256.Length);
    }

    [Fact]
    public async Task WriteChunkAndFinalize_ValidData_AssemblesAndVerifiesSuccessfully()
    {
        var jobId = Guid.NewGuid();
        var relPath = "downloads/document.txt";
        var content = "DropZone secure chunk assembly test payload.";
        var bytes = Encoding.UTF8.GetBytes(content);
        var expectedSha = _security.ComputeSha256(bytes);

        // Write first chunk
        var chunk1 = bytes.Take(20).ToArray();
        await _storage.WriteChunkAsync(jobId, relPath, 0, chunk1);

        // Check resume offset
        var resumeOffset = await _storage.GetExistingResumeOffsetAsync(jobId, relPath, bytes.Length, expectedSha);
        Assert.True(resumeOffset >= 0);

        // Write remaining chunk
        var chunk2 = bytes.Skip(20).ToArray();
        await _storage.WriteChunkAsync(jobId, relPath, 20, chunk2);

        // Finalize
        var success = await _storage.FinalizeFileAsync(jobId, relPath, expectedSha);
        Assert.True(success);

        var finalPath = _storage.GetDownloadPath(relPath);
        Assert.True(File.Exists(finalPath));
        var actualText = await File.ReadAllTextAsync(finalPath);
        Assert.Equal(content, actualText);
    }

    [Fact]
    public async Task FinalizeFile_CorruptData_QuarantinesFile()
    {
        var jobId = Guid.NewGuid();
        var relPath = "bad_file.dat";
        var bytes = Encoding.UTF8.GetBytes("tampered data bytes");
        await _storage.WriteChunkAsync(jobId, relPath, 0, bytes);

        // Provide mismatched SHA-256
        var invalidExpectedSha = "0000000000000000000000000000000000000000000000000000000000000000";
        var success = await _storage.FinalizeFileAsync(jobId, relPath, invalidExpectedSha);

        Assert.False(success);
        var finalPath = _storage.GetDownloadPath(relPath);
        Assert.False(File.Exists(finalPath)); // Should NOT be moved to final target

        var quarantineFile = $"{finalPath}.{jobId:N}.corrupt";
        Assert.True(File.Exists(quarantineFile)); // Should be isolated in quarantine
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, recursive: true);
            }
        }
        catch { }
    }
}

