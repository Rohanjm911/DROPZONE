using System.Text.Json.Serialization;

namespace DropZone.Domain.Models;

public enum ThreatRiskLevel
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public sealed class SuspiciousFileAnalysis
{
    public string FileName { get; set; } = string.Empty;
    public bool IsSuspicious { get; set; }
    public ThreatRiskLevel RiskLevel { get; set; } = ThreatRiskLevel.None;
    public string Reason { get; set; } = string.Empty;

    public static SuspiciousFileAnalysis Clean(string fileName) => new()
    {
        FileName = fileName,
        IsSuspicious = false,
        RiskLevel = ThreatRiskLevel.None,
        Reason = "No threats detected"
    };

    public static SuspiciousFileAnalysis Flagged(string fileName, ThreatRiskLevel risk, string reason) => new()
    {
        FileName = fileName,
        IsSuspicious = true,
        RiskLevel = risk,
        Reason = reason
    };
}

public sealed class QuarantinedFileRecord
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonPropertyName("original_file_name")]
    public string OriginalFileName { get; set; } = string.Empty;

    [JsonPropertyName("original_relative_path")]
    public string OriginalRelativePath { get; set; } = string.Empty;

    [JsonPropertyName("quarantined_file_path")]
    public string QuarantinedFilePath { get; set; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("threat_reason")]
    public string ThreatReason { get; set; } = string.Empty;

    [JsonPropertyName("source_device_id")]
    public string SourceDeviceId { get; set; } = string.Empty;

    [JsonPropertyName("quarantined_at")]
    public DateTime QuarantinedAt { get; set; } = DateTime.UtcNow;
}

