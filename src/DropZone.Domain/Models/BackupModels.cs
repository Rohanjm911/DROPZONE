using System.Text.Json.Serialization;

namespace DropZone.Domain.Models;

public sealed class DropZoneBackupPackage
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("exported_at")]
    public DateTime ExportedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("local_device_id")]
    public string LocalDeviceId { get; set; } = string.Empty;

    [JsonPropertyName("devices")]
    public List<Device> Devices { get; set; } = new();

    [JsonPropertyName("transfer_history")]
    public List<TransferJob> TransferHistory { get; set; } = new();

    [JsonPropertyName("settings")]
    public Dictionary<string, string> Settings { get; set; } = new();
}

