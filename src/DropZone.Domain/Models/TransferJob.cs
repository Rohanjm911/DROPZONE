using DropZone.Domain.Enums;

namespace DropZone.Domain.Models;

public sealed class TransferJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string PeerDeviceId { get; set; } = string.Empty;
    public string PeerDeviceName { get; set; } = string.Empty;
    public string PeerEndpoint { get; set; } = string.Empty;
    public TransferDirection Direction { get; set; }
    public TransferState State { get; set; } = TransferState.Pending;
    public long TotalBytes { get; set; }
    public long TransferredBytes { get; set; }
    public double SpeedBytesPerSec { get; set; }
    public int EtaSeconds { get; set; }
    public List<FileItem> Files { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }

    public double ProgressPercentage => TotalBytes > 0 
        ? Math.Clamp((double)TransferredBytes / TotalBytes * 100.0, 0.0, 100.0) 
        : 0.0;
}

public sealed class Device
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public ushort Port { get; set; }
    public DeviceTrustLevel TrustLevel { get; set; } = DeviceTrustLevel.Prompt;
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    public string PublicKeyFingerprint { get; set; } = string.Empty;
    public DevicePermissions Permissions { get; set; } = new();
    public bool IsOnline => (DateTime.UtcNow - LastSeen).TotalSeconds < 30;
}

