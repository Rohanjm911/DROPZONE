using DropZone.Domain.Enums;
using DropZone.Domain.Interfaces;
using DropZone.Domain.Models;
using Microsoft.Data.Sqlite;

namespace DropZone.Infrastructure.Database;

public sealed class SqliteDeviceRepository : IDeviceRepository
{
    private readonly string _connectionString;

    public SqliteDeviceRepository(string databasePath)
    {
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

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS devices (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                ip_address TEXT NOT NULL,
                port INTEGER NOT NULL,
                trust_level INTEGER NOT NULL,
                last_seen TEXT NOT NULL,
                public_key_fingerprint TEXT NOT NULL
            );
        """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpsertDeviceAsync(Device device, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO devices (id, name, ip_address, port, trust_level, last_seen, public_key_fingerprint)
            VALUES (@id, @name, @ip, @port, @trust, @last_seen, @fp)
            ON CONFLICT(id) DO UPDATE SET
                name = excluded.name,
                ip_address = excluded.ip_address,
                port = excluded.port,
                last_seen = excluded.last_seen;
        """;

        cmd.Parameters.AddWithValue("@id", device.Id);
        cmd.Parameters.AddWithValue("@name", device.Name);
        cmd.Parameters.AddWithValue("@ip", device.IpAddress);
        cmd.Parameters.AddWithValue("@port", device.Port);
        cmd.Parameters.AddWithValue("@trust", (int)device.TrustLevel);
        cmd.Parameters.AddWithValue("@last_seen", device.LastSeen.ToString("O"));
        cmd.Parameters.AddWithValue("@fp", device.PublicKeyFingerprint);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<Device?> GetDeviceAsync(string deviceId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM devices WHERE id = @id LIMIT 1;";
        cmd.Parameters.AddWithValue("@id", deviceId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return MapReaderToDevice(reader);
        }
        return null;
    }

    public async Task<IReadOnlyList<Device>> GetAllDevicesAsync(CancellationToken ct = default)
    {
        var list = new List<Device>();
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM devices ORDER BY last_seen DESC;";

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            list.Add(MapReaderToDevice(reader));
        }
        return list;
    }

    public async Task UpdateTrustLevelAsync(string deviceId, DeviceTrustLevel trustLevel, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE devices SET trust_level = @trust WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", deviceId);
        cmd.Parameters.AddWithValue("@trust", (int)trustLevel);

        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static Device MapReaderToDevice(SqliteDataReader reader)
    {
        return new Device
        {
            Id = reader.GetString(reader.GetOrdinal("id")),
            Name = reader.GetString(reader.GetOrdinal("name")),
            IpAddress = reader.GetString(reader.GetOrdinal("ip_address")),
            Port = (ushort)reader.GetInt32(reader.GetOrdinal("port")),
            TrustLevel = (DeviceTrustLevel)reader.GetInt32(reader.GetOrdinal("trust_level")),
            LastSeen = DateTime.Parse(reader.GetString(reader.GetOrdinal("last_seen"))),
            PublicKeyFingerprint = reader.GetString(reader.GetOrdinal("public_key_fingerprint"))
        };
    }
}

