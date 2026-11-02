using System.Text.Json.Serialization;

namespace DropZone.Domain.Models;

public sealed class IdentityKeyPair
{
    [JsonPropertyName("public_key")]
    public string PublicKeyHex { get; set; } = string.Empty;

    [JsonPropertyName("fingerprint")]
    public string Fingerprint { get; set; } = string.Empty;
}

public sealed class PairingPinInfo
{
    [JsonPropertyName("peer_device_id")]
    public string PeerDeviceId { get; set; } = string.Empty;

    [JsonPropertyName("peer_device_name")]
    public string PeerDeviceName { get; set; } = string.Empty;

    [JsonPropertyName("pin_code")]
    public string PinCode { get; set; } = string.Empty;

    [JsonPropertyName("fingerprint")]
    public string Fingerprint { get; set; } = string.Empty;

    [JsonPropertyName("is_initiator")]
    public bool IsInitiator { get; set; }
}

public sealed class PairingResult
{
    [JsonPropertyName("peer_device_id")]
    public string PeerDeviceId { get; set; } = string.Empty;

    [JsonPropertyName("peer_device_name")]
    public string PeerDeviceName { get; set; } = string.Empty;

    [JsonPropertyName("fingerprint")]
    public string Fingerprint { get; set; } = string.Empty;

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

