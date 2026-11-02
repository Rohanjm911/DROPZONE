using System.Text.Json.Serialization;

namespace DropZone.Domain.Models;

public enum NatCandidateType
{
    Host,
    ServerReflexive,
    Relayed
}

public sealed class NatCandidate
{
    [JsonPropertyName("candidate_type")]
    public string CandidateTypeStr { get; set; } = "host";

    [JsonPropertyName("ip")]
    public string Ip { get; set; } = string.Empty;

    [JsonPropertyName("port")]
    public ushort Port { get; set; }

    [JsonPropertyName("priority")]
    public uint Priority { get; set; }

    [JsonIgnore]
    public NatCandidateType CandidateType => CandidateTypeStr.ToLowerInvariant() switch
    {
        "server_reflexive" or "serverreflexive" => NatCandidateType.ServerReflexive,
        "relayed" => NatCandidateType.Relayed,
        _ => NatCandidateType.Host
    };

    public override string ToString() => $"{CandidateType} -> {Ip}:{Port} (Priority: {Priority})";
}

