using System.Text.Json;
using DropZone.Domain.Enums;
using DropZone.Domain.Models;
using Xunit;

namespace DropZone.UnitTests;

public class DomainTests
{
    [Fact]
    public void TransferJob_ProgressPercentage_CalculatesCorrectly()
    {
        var job = new TransferJob
        {
            TotalBytes = 1000,
            TransferredBytes = 250
        };

        Assert.Equal(25.0, job.ProgressPercentage);

        job.TransferredBytes = 1000;
        Assert.Equal(100.0, job.ProgressPercentage);

        job.TransferredBytes = 1200; // Over-reporting should be clamped
        Assert.Equal(100.0, job.ProgressPercentage);
    }

    [Fact]
    public void TransferJob_ProgressPercentage_HandlesZeroTotalBytes()
    {
        var job = new TransferJob
        {
            TotalBytes = 0,
            TransferredBytes = 0
        };

        Assert.Equal(0.0, job.ProgressPercentage);
    }

    [Fact]
    public void TransferManifest_SerializationRoundTrip_PreservesAllFields()
    {
        var manifest = new TransferManifest
        {
            JobId = Guid.NewGuid(),
            SenderDeviceId = "dev-test-1",
            SenderDeviceName = "Rohan Laptop",
            TotalBytes = 5242880,
            Files = new List<FileItem>
            {
                new()
                {
                    Index = 0,
                    RelativePath = "Docs/Report.pdf",
                    SizeBytes = 5242880,
                    Sha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                    MimeType = "application/pdf"
                }
            }
        };

        var json = JsonSerializer.Serialize(manifest);
        var deserialized = JsonSerializer.Deserialize<TransferManifest>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(manifest.JobId, deserialized.JobId);
        Assert.Equal(manifest.SenderDeviceId, deserialized.SenderDeviceId);
        Assert.Equal(manifest.TotalBytes, deserialized.TotalBytes);
        Assert.Equal(1, deserialized.FileCount);
        Assert.Equal("Docs/Report.pdf", deserialized.Files[0].RelativePath);
    }

    [Fact]
    public void Device_IsOnline_CalculatesBasedOnLastSeen()
    {
        var onlineDevice = new Device
        {
            Id = "d1",
            Name = "Online Peer",
            LastSeen = DateTime.UtcNow.AddSeconds(-5)
        };
        Assert.True(onlineDevice.IsOnline);

        var offlineDevice = new Device
        {
            Id = "d2",
            Name = "Offline Peer",
            LastSeen = DateTime.UtcNow.AddSeconds(-45)
        };
        Assert.False(offlineDevice.IsOnline);
    }
}

