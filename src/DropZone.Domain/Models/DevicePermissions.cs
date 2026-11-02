using System.Text.Json.Serialization;

namespace DropZone.Domain.Models;

public sealed class DevicePermissions
{
    [JsonPropertyName("device_id")]
    public string DeviceId { get; set; } = string.Empty;

    [JsonPropertyName("allow_auto_accept")]
    public bool AllowAutoAccept { get; set; } = true;

    [JsonPropertyName("max_auto_accept_bytes")]
    public long MaxAutoAcceptBytes { get; set; } = 104_857_600; // 100 MB default threshold

    [JsonPropertyName("scoped_download_folder")]
    public string ScopedDownloadFolder { get; set; } = string.Empty;

    [JsonPropertyName("require_pin_verification")]
    public bool RequirePinVerification { get; set; } = false;

    [JsonPropertyName("revoked_at")]
    public DateTime? RevokedAt { get; set; }

    [JsonIgnore]
    public bool IsRevoked => RevokedAt.HasValue;

    public static DevicePermissions Default(string deviceId) => new()
    {
        DeviceId = deviceId,
        AllowAutoAccept = true,
        MaxAutoAcceptBytes = 104_857_600,
        ScopedDownloadFolder = string.Empty,
        RequirePinVerification = false,
        RevokedAt = null
    };
}

