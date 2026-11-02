using System.Text.Json.Serialization;

namespace DropZone.Domain.Models;

public enum NetworkRouteType
{
    DirectLan,
    ReflexiveP2P,
    TurnRelay
}

public sealed class TransferTelemetry
{
    [JsonPropertyName("job_id")]
    public string JobId { get; set; } = string.Empty;

    [JsonPropertyName("rtt_ms")]
    public uint RttMs { get; set; }

    [JsonPropertyName("speed_bytes_per_sec")]
    public double SpeedBytesPerSec { get; set; }

    [JsonPropertyName("chunk_size")]
    public uint ChunkSize { get; set; }

    [JsonPropertyName("route")]
    public string RouteStr { get; set; } = "direct_lan";

    [JsonPropertyName("packet_loss_pct")]
    public float PacketLossPct { get; set; }

    [JsonIgnore]
    public NetworkRouteType Route => RouteStr.ToLowerInvariant() switch
    {
        "reflexive_p2p" or "reflexivep2p" => NetworkRouteType.ReflexiveP2P,
        "turn_relay" or "turnrelay" => NetworkRouteType.TurnRelay,
        _ => NetworkRouteType.DirectLan
    };

    public string FormattedChunkSize => $"{ChunkSize / 1024} KB";
    public string FormattedLatency => $"{RttMs} ms";
}

