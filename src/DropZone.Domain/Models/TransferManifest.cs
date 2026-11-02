using System.Text.Json.Serialization;

namespace DropZone.Domain.Models;

public sealed class FileItem
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("relative_path")]
    public string RelativePath { get; set; } = string.Empty;

    [JsonPropertyName("size_bytes")]
    public long SizeBytes { get; set; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("mime_type")]
    public string MimeType { get; set; } = "application/octet-stream";
}

public sealed class TransferManifest
{
    [JsonPropertyName("job_id")]
    public Guid JobId { get; set; }

    [JsonPropertyName("sender_device_id")]
    public string SenderDeviceId { get; set; } = string.Empty;

    [JsonPropertyName("sender_device_name")]
    public string SenderDeviceName { get; set; } = string.Empty;

    [JsonPropertyName("total_bytes")]
    public long TotalBytes { get; set; }

    [JsonPropertyName("file_count")]
    public int FileCount => Files?.Count ?? 0;

    [JsonPropertyName("files")]
    public List<FileItem> Files { get; set; } = new();

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}

