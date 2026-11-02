using System.Security.Cryptography;
using DropZone.Domain.Enums;
using DropZone.Domain.Models;
using DropZone.Infrastructure.Database;
using DropZone.Infrastructure.Storage;
using Xunit;

namespace DropZone.UnitTests;

public class BackupServiceTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteDeviceRepository _deviceRepo;
    private readonly SqliteTransferRepository _transferRepo;
    private readonly BackupService _backupService;

    public BackupServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"dz_backup_test_{Guid.NewGuid():N}.db");
        _deviceRepo = new SqliteDeviceRepository(_dbPath);
        _transferRepo = new SqliteTransferRepository(_dbPath);
        _backupService = new BackupService(_transferRepo, _deviceRepo);
    }

    [Fact]
    public async Task Backup_EncryptedRoundtrip_RestoresDataAccurately()
    {
        await _deviceRepo.InitializeAsync();
        await _transferRepo.InitializeAsync();

        // Seed device
        var dev = new Device
        {
            Id = "backup-dev-1",
            Name = "MacBook Pro M3",
            IpAddress = "192.168.1.50",
            Port = 52490,
            TrustLevel = DeviceTrustLevel.Trusted,
            PublicKeyFingerprint = "fingerprint-12345"
        };
        await _deviceRepo.UpsertDeviceAsync(dev);

        // Seed transfer job
        var job = new TransferJob
        {
            Id = Guid.NewGuid(),
            PeerDeviceId = dev.Id,
            PeerDeviceName = dev.Name,
            TotalBytes = 1048576,
            State = TransferState.Completed,
            Files = new List<FileItem>
            {
                new() { Index = 0, RelativePath = "test.pdf", SizeBytes = 1048576, Sha256 = "abc" }
            }
        };
        await _transferRepo.AddJobAsync(job);

        // 1. Create encrypted backup
        var passphrase = "UltraSecureMasterPassphrase_2026!";
        var encryptedBytes = await _backupService.CreateEncryptedBackupAsync(passphrase);

        Assert.NotNull(encryptedBytes);
        Assert.True(encryptedBytes.Length > 100);

        // 2. Restore backup with correct passphrase
        var restored = await _backupService.RestoreEncryptedBackupAsync(encryptedBytes, passphrase);
        Assert.NotNull(restored);
        Assert.Single(restored.Devices);
        Assert.Equal("backup-dev-1", restored.Devices[0].Id);
        Assert.Single(restored.TransferHistory);
        Assert.Equal(job.Id, restored.TransferHistory[0].Id);

        // 3. Attempt restore with wrong passphrase -> must fail with CryptographicException
        await Assert.ThrowsAsync<CryptographicException>(async () =>
        {
            await _backupService.RestoreEncryptedBackupAsync(encryptedBytes, "WrongPassphrase123!");
        });
    }

    [Fact]
    public async Task Backup_CorruptedCiphertext_FailsVerification()
    {
        await _deviceRepo.InitializeAsync();
        await _transferRepo.InitializeAsync();

        var passphrase = "ValidPassphrase_999!";
        var encryptedBytes = await _backupService.CreateEncryptedBackupAsync(passphrase);

        // Corrupt a byte in the payload
        encryptedBytes[^1] ^= 0xFF;

        await Assert.ThrowsAsync<CryptographicException>(async () =>
        {
            await _backupService.RestoreEncryptedBackupAsync(encryptedBytes, passphrase);
        });
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
            var shm = $"{_dbPath}-shm";
            if (File.Exists(shm)) File.Delete(shm);
            var wal = $"{_dbPath}-wal";
            if (File.Exists(wal)) File.Delete(wal);
        }
        catch { }
    }
}
