namespace DropZone.Domain.Models;

public sealed class TransferChunk
{
    public Guid JobId { get; set; }
    public int FileIndex { get; set; }
    public long Offset { get; set; }
    public int Length { get; set; }
    public byte[] Sha256Checksum { get; set; } = Array.Empty<byte>();
    public byte[] Data { get; set; } = Array.Empty<byte>();
}

