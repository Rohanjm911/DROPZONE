using DropZone.Domain.Enums;
using DropZone.Domain.Models;
using DropZone.Infrastructure.Database;
using Xunit;

namespace DropZone.UnitTests;

public class SqliteRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteTransferRepository _transferRepo;
    private readonly SqliteDeviceRepository _deviceRepo;

    public SqliteRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"dropzone_test_{Guid.NewGuid():N}.db");
        _transferRepo = new SqliteTransferRepository(_dbPath);
        _deviceRepo = new SqliteDeviceRepository(_dbPath);
    }

    [Fact]
    public async Task TransferRepository_CrudOperations_WorkCorrectly()
    {
        await _transferRepo.InitializeAsync();

        var job = new TransferJob
        {
            Id = Guid.NewGuid(),
            PeerDeviceId = "dev-1",
            PeerDeviceName = "MacBook Pro",
            PeerEndpoint = "192.168.1.50:52490",
            Direction = TransferDirection.Outbound,
            State = TransferState.Pending,
            TotalBytes = 1048576,
            TransferredBytes = 0,
            SpeedBytesPerSec = 0,
            EtaSeconds = 0,
            Files = new List<FileItem>
            {
                new() { Index = 0, RelativePath = "test.txt", SizeBytes = 1048576, Sha256 = "dummy" }
            }
        };

        // Add
        await _transferRepo.AddJobAsync(job);

        // Read
        var retrieved = await _transferRepo.GetJobAsync(job.Id);
        Assert.NotNull(retrieved);
        Assert.Equal(job.Id, retrieved.Id);
        Assert.Equal("MacBook Pro", retrieved.PeerDeviceName);
        Assert.Equal(TransferState.Pending, retrieved.State);
        Assert.Single(retrieved.Files);

        // Update progress
        await _transferRepo.UpdateJobProgressAsync(job.Id, 524288, 2500000.0, 1);
        var progressUpdated = await _transferRepo.GetJobAsync(job.Id);
        Assert.NotNull(progressUpdated);
        Assert.Equal(524288, progressUpdated.TransferredBytes);
        Assert.Equal(2500000.0, progressUpdated.SpeedBytesPerSec);

        // Update state
        await _transferRepo.UpdateJobStateAsync(job.Id, TransferState.Completed);
        var completed = await _transferRepo.GetJobAsync(job.Id);
        Assert.NotNull(completed);
        Assert.Equal(TransferState.Completed, completed.State);
        Assert.NotNull(completed.CompletedAt);

        // List
        var list = await _transferRepo.GetAllJobsAsync();
        Assert.Contains(list, j => j.Id == job.Id);

        // Delete
        await _transferRepo.DeleteJobAsync(job.Id);
        var deleted = await _transferRepo.GetJobAsync(job.Id);
        Assert.Null(deleted);
    }

    [Fact]
    public async Task DeviceRepository_UpsertAndTrustManagement_WorksCorrectly()
    {
        await _deviceRepo.InitializeAsync();

        var device = new Device
        {
            Id = "device-uuid-99",
            Name = "Linux Workstation",
            IpAddress = "192.168.1.120",
            Port = 52490,
            TrustLevel = DeviceTrustLevel.Prompt,
            PublicKeyFingerprint = "fp-abc-123"
        };

        await _deviceRepo.UpsertDeviceAsync(device);

        var retrieved = await _deviceRepo.GetDeviceAsync(device.Id);
        Assert.NotNull(retrieved);
        Assert.Equal("Linux Workstation", retrieved.Name);
        Assert.Equal(DeviceTrustLevel.Prompt, retrieved.TrustLevel);

        // Promote to Trusted
        await _deviceRepo.UpdateTrustLevelAsync(device.Id, DeviceTrustLevel.Trusted);
        var trusted = await _deviceRepo.GetDeviceAsync(device.Id);
        Assert.NotNull(trusted);
        Assert.Equal(DeviceTrustLevel.Trusted, trusted.TrustLevel);

        var all = await _deviceRepo.GetAllDevicesAsync();
        Assert.Contains(all, d => d.Id == device.Id);
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

