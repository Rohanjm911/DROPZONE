using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DropZone.Domain.Interfaces;
using DropZone.Domain.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace DropZone.Infrastructure.Enterprise;

public sealed class ComplianceAuditService : IComplianceAuditService
{
    private readonly string _connectionString;
    private readonly ILogger<ComplianceAuditService>? _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private const string GenesisHash = "0000000000000000000000000000000000000000000000000000000000000000";

    public ComplianceAuditService(string dbPath, ILogger<ComplianceAuditService>? logger = null)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = @"
            CREATE TABLE IF NOT EXISTS compliance_audit_ledger (
                sequence_num INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp_utc TEXT NOT NULL,
                event_type INTEGER NOT NULL,
                actor_device_id TEXT NOT NULL,
                target_device_id TEXT NOT NULL,
                details TEXT NOT NULL,
                prev_hash TEXT NOT NULL,
                entry_hash TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_audit_timestamp ON compliance_audit_ledger(timestamp_utc);
        ";

        await using var cmd = new SqliteCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<ComplianceAuditEntry> LogEventAsync(
        AuditEventType eventType,
        string actorDeviceId,
        string targetDeviceId,
        string details,
        CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);

            // Fetch last entry hash
            var lastHashSql = "SELECT sequence_num, entry_hash FROM compliance_audit_ledger ORDER BY sequence_num DESC LIMIT 1;";
            long nextSeq = 1;
            string prevHash = GenesisHash;

            await using (var lastCmd = new SqliteCommand(lastHashSql, conn))
            await using (var reader = await lastCmd.ExecuteReaderAsync(ct))
            {
                if (await reader.ReadAsync(ct))
                {
                    nextSeq = reader.GetInt64(0) + 1;
                    prevHash = reader.GetString(1);
                }
            }

            var nowUtc = DateTime.UtcNow;
            var nowStr = nowUtc.ToString("O");
            var computedHash = ComputeEntryHash(nextSeq, nowStr, (int)eventType, actorDeviceId, targetDeviceId, details, prevHash);

            var insertSql = @"
                INSERT INTO compliance_audit_ledger 
                (sequence_num, timestamp_utc, event_type, actor_device_id, target_device_id, details, prev_hash, entry_hash)
                VALUES (@seq, @ts, @type, @actor, @target, @details, @prev, @hash);
            ";

            await using (var insertCmd = new SqliteCommand(insertSql, conn))
            {
                insertCmd.Parameters.AddWithValue("@seq", nextSeq);
                insertCmd.Parameters.AddWithValue("@ts", nowStr);
                insertCmd.Parameters.AddWithValue("@type", (int)eventType);
                insertCmd.Parameters.AddWithValue("@actor", actorDeviceId);
                insertCmd.Parameters.AddWithValue("@target", targetDeviceId);
                insertCmd.Parameters.AddWithValue("@details", details);
                insertCmd.Parameters.AddWithValue("@prev", prevHash);
                insertCmd.Parameters.AddWithValue("@hash", computedHash);
                await insertCmd.ExecuteNonQueryAsync(ct);
            }

            var entry = new ComplianceAuditEntry
            {
                SequenceNumber = nextSeq,
                TimestampUtc = nowUtc,
                EventType = eventType,
                ActorDeviceId = actorDeviceId,
                TargetDeviceId = targetDeviceId,
                Details = details,
                PreviousEntryHash = prevHash,
                EntryHash = computedHash
            };

            _logger?.LogInformation("[Compliance Audit] Entry #{Seq} [{Event}]: {Details} (Hash: {Hash})",
                nextSeq, eventType, details, computedHash[..12]);

            return entry;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<AuditChainVerificationResult> VerifyAuditChainIntegrityAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = "SELECT sequence_num, timestamp_utc, event_type, actor_device_id, target_device_id, details, prev_hash, entry_hash FROM compliance_audit_ledger ORDER BY sequence_num ASC;";
        await using var cmd = new SqliteCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        string expectedPrevHash = GenesisHash;
        long count = 0;
        long expectedSeq = 1;

        while (await reader.ReadAsync(ct))
        {
            count++;
            var seq = reader.GetInt64(0);
            var ts = reader.GetString(1);
            var eventType = reader.GetInt32(2);
            var actor = reader.GetString(3);
            var target = reader.GetString(4);
            var details = reader.GetString(5);
            var prevHash = reader.GetString(6);
            var entryHash = reader.GetString(7);

            // Sequence gap or out-of-order check
            if (seq != expectedSeq)
            {
                return new AuditChainVerificationResult
                {
                    IsValid = false,
                    TotalEntriesVerified = count - 1,
                    BrokenSequenceNumber = seq,
                    ErrorMessage = $"Sequence gap detected: expected #{expectedSeq}, encountered #{seq}."
                };
            }

            // Chain previous hash match check
            if (prevHash != expectedPrevHash)
            {
                return new AuditChainVerificationResult
                {
                    IsValid = false,
                    TotalEntriesVerified = count - 1,
                    BrokenSequenceNumber = seq,
                    ErrorMessage = $"Previous hash mismatch at sequence #{seq}. Expected '{expectedPrevHash[..12]}...', found '{prevHash[..12]}...'."
                };
            }

            // Entry hash recalculation check
            var computed = ComputeEntryHash(seq, ts, eventType, actor, target, details, prevHash);
            if (computed != entryHash)
            {
                return new AuditChainVerificationResult
                {
                    IsValid = false,
                    TotalEntriesVerified = count - 1,
                    BrokenSequenceNumber = seq,
                    ErrorMessage = $"Tampered entry detected at sequence #{seq}. Content does not match cryptographic hash."
                };
            }

            expectedPrevHash = entryHash;
            expectedSeq++;
        }

        return new AuditChainVerificationResult
        {
            IsValid = true,
            TotalEntriesVerified = count,
            ErrorMessage = "All audit chain hashes mathematically verified."
        };
    }

    public async Task<IReadOnlyList<ComplianceAuditEntry>> GetAuditLogAsync(int limit = 200, CancellationToken ct = default)
    {
        var list = new List<ComplianceAuditEntry>();
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        var sql = "SELECT sequence_num, timestamp_utc, event_type, actor_device_id, target_device_id, details, prev_hash, entry_hash FROM compliance_audit_ledger ORDER BY sequence_num DESC LIMIT @limit;";
        await using var cmd = new SqliteCommand(sql, conn);
        cmd.Parameters.AddWithValue("@limit", limit);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(new ComplianceAuditEntry
            {
                SequenceNumber = reader.GetInt64(0),
                TimestampUtc = DateTime.Parse(reader.GetString(1)),
                EventType = (AuditEventType)reader.GetInt32(2),
                ActorDeviceId = reader.GetString(3),
                TargetDeviceId = reader.GetString(4),
                Details = reader.GetString(5),
                PreviousEntryHash = reader.GetString(6),
                EntryHash = reader.GetString(7)
            });
        }

        return list;
    }

    public async Task<string> ExportAuditReportJsonAsync(CancellationToken ct = default)
    {
        var entries = await GetAuditLogAsync(5000, ct);
        var verification = await VerifyAuditChainIntegrityAsync(ct);

        var report = new
        {
            ReportGeneratedAtUtc = DateTime.UtcNow,
            ComplianceStandard = "DROPZONE SOC2/HIPAA Cryptographic Hash-Chained Audit Ledger",
            ChainIntegrity = verification,
            TotalAuditedEvents = entries.Count,
            Events = entries
        };

        return JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string ComputeEntryHash(
        long sequence,
        string timestamp,
        int eventType,
        string actor,
        string target,
        string details,
        string prevHash)
    {
        var payload = $"{sequence}:{timestamp}:{eventType}:{actor}:{target}:{details}:{prevHash}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
