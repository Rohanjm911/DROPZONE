using System.Text.Json;
using DropZone.Domain.Enums;
using DropZone.Domain.Interfaces;
using DropZone.Domain.Models;
using Microsoft.Data.Sqlite;

namespace DropZone.Infrastructure.Database;

public sealed class SqliteTransferRepository : ITransferRepository
{
    private readonly string _connectionString;

    public SqliteTransferRepository(string databasePath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        // WAL mode for maximum write concurrency
        await using var walCmd = conn.CreateCommand();
        walCmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
        await walCmd.ExecuteNonQueryAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS transfer_jobs (
                id TEXT PRIMARY KEY,
                peer_device_id TEXT NOT NULL,
                peer_device_name TEXT NOT NULL,
                peer_endpoint TEXT NOT NULL,
                direction INTEGER NOT NULL,
                state INTEGER NOT NULL,
                total_bytes INTEGER NOT NULL,
                transferred_bytes INTEGER NOT NULL,
                speed_bps REAL NOT NULL,
                eta_seconds INTEGER NOT NULL,
                files_json TEXT NOT NULL,
                created_at TEXT NOT NULL,
                completed_at TEXT,
                error_message TEXT
            );

            CREATE INDEX IF NOT EXISTS idx_jobs_created_at ON transfer_jobs(created_at DESC);
        """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task AddJobAsync(TransferJob job, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO transfer_jobs 
            (id, peer_device_id, peer_device_name, peer_endpoint, direction, state, 
             total_bytes, transferred_bytes, speed_bps, eta_seconds, files_json, created_at, completed_at, error_message)
            VALUES 
            (@id, @peer_id, @peer_name, @peer_endpoint, @dir, @state, 
             @total, @transferred, @speed, @eta, @files, @created, @completed, @error);
        """;

        cmd.Parameters.AddWithValue("@id", job.Id.ToString());
        cmd.Parameters.AddWithValue("@peer_id", job.PeerDeviceId);
        cmd.Parameters.AddWithValue("@peer_name", job.PeerDeviceName);
        cmd.Parameters.AddWithValue("@peer_endpoint", job.PeerEndpoint);
        cmd.Parameters.AddWithValue("@dir", (int)job.Direction);
        cmd.Parameters.AddWithValue("@state", (int)job.State);
        cmd.Parameters.AddWithValue("@total", job.TotalBytes);
        cmd.Parameters.AddWithValue("@transferred", job.TransferredBytes);
        cmd.Parameters.AddWithValue("@speed", job.SpeedBytesPerSec);
        cmd.Parameters.AddWithValue("@eta", job.EtaSeconds);
        cmd.Parameters.AddWithValue("@files", JsonSerializer.Serialize(job.Files));
        cmd.Parameters.AddWithValue("@created", job.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@completed", (object?)job.CompletedAt?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@error", (object?)job.ErrorMessage ?? DBNull.Value);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateJobStateAsync(Guid jobId, TransferState state, string? error = null, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE transfer_jobs 
            SET state = @state, 
                error_message = @error,
                completed_at = CASE WHEN @state = 4 THEN @now ELSE completed_at END
            WHERE id = @id;
        """;

        cmd.Parameters.AddWithValue("@id", jobId.ToString());
        cmd.Parameters.AddWithValue("@state", (int)state);
        cmd.Parameters.AddWithValue("@error", (object?)error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@now", DateTime.UtcNow.ToString("O"));

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpdateJobProgressAsync(Guid jobId, long transferredBytes, double speedBps, int etaSec, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE transfer_jobs 
            SET transferred_bytes = @transferred, speed_bps = @speed, eta_seconds = @eta
            WHERE id = @id;
        """;

        cmd.Parameters.AddWithValue("@id", jobId.ToString());
        cmd.Parameters.AddWithValue("@transferred", transferredBytes);
        cmd.Parameters.AddWithValue("@speed", speedBps);
        cmd.Parameters.AddWithValue("@eta", etaSec);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<TransferJob?> GetJobAsync(Guid jobId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM transfer_jobs WHERE id = @id LIMIT 1;";
        cmd.Parameters.AddWithValue("@id", jobId.ToString());

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return MapReaderToJob(reader);
        }
        return null;
    }

    public async Task<IReadOnlyList<TransferJob>> GetAllJobsAsync(int limit = 100, CancellationToken ct = default)
    {
        var list = new List<TransferJob>();
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM transfer_jobs ORDER BY created_at DESC LIMIT @limit;";
        cmd.Parameters.AddWithValue("@limit", limit);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(MapReaderToJob(reader));
        }
        return list;
    }

    public async Task DeleteJobAsync(Guid jobId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM transfer_jobs WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", jobId.ToString());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static TransferJob MapReaderToJob(SqliteDataReader reader)
    {
        var filesJson = reader.GetString(reader.GetOrdinal("files_json"));
        var files = JsonSerializer.Deserialize<List<FileItem>>(filesJson) ?? new List<FileItem>();

        return new TransferJob
        {
            Id = Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
            PeerDeviceId = reader.GetString(reader.GetOrdinal("peer_device_id")),
            PeerDeviceName = reader.GetString(reader.GetOrdinal("peer_device_name")),
            PeerEndpoint = reader.GetString(reader.GetOrdinal("peer_endpoint")),
            Direction = (TransferDirection)reader.GetInt32(reader.GetOrdinal("direction")),
            State = (TransferState)reader.GetInt32(reader.GetOrdinal("state")),
            TotalBytes = reader.GetInt64(reader.GetOrdinal("total_bytes")),
            TransferredBytes = reader.GetInt64(reader.GetOrdinal("transferred_bytes")),
            SpeedBytesPerSec = reader.GetDouble(reader.GetOrdinal("speed_bps")),
            EtaSeconds = reader.GetInt32(reader.GetOrdinal("eta_seconds")),
            Files = files,
            CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at"))),
            CompletedAt = reader.IsDBNull(reader.GetOrdinal("completed_at")) 
                ? null 
                : DateTime.Parse(reader.GetString(reader.GetOrdinal("completed_at"))),
            ErrorMessage = reader.IsDBNull(reader.GetOrdinal("error_message")) 
                ? null 
                : reader.GetString(reader.GetOrdinal("error_message"))
        };
    }
}

