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

            CREATE TABLE IF NOT EXISTS device_permissions (
                device_id TEXT PRIMARY KEY,
                allow_auto_accept INTEGER NOT NULL DEFAULT 1,
                max_auto_accept_bytes INTEGER NOT NULL DEFAULT 104857600,
                scoped_download_folder TEXT,
                require_pin_verification INTEGER NOT NULL DEFAULT 0,
                revoked_at TEXT
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

    public Task RevokeDeviceAsync(string deviceId, CancellationToken ct = default)
    {
        return UpdateTrustLevelAsync(deviceId, DeviceTrustLevel.Prompt, ct);
    }

    public Task BlockDeviceAsync(string deviceId, CancellationToken ct = default)
    {
        return UpdateTrustLevelAsync(deviceId, DeviceTrustLevel.Blocked, ct);
    }

    public async Task<DevicePermissions> GetPermissionsAsync(string deviceId, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM device_permissions WHERE device_id = @id LIMIT 1;";
        cmd.Parameters.AddWithValue("@id", deviceId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            var revokedStr = reader.IsDBNull(reader.GetOrdinal("revoked_at")) ? null : reader.GetString(reader.GetOrdinal("revoked_at"));
            return new DevicePermissions
            {
                DeviceId = deviceId,
                AllowAutoAccept = reader.GetInt32(reader.GetOrdinal("allow_auto_accept")) == 1,
                MaxAutoAcceptBytes = reader.GetInt64(reader.GetOrdinal("max_auto_accept_bytes")),
                ScopedDownloadFolder = reader.IsDBNull(reader.GetOrdinal("scoped_download_folder")) ? "" : reader.GetString(reader.GetOrdinal("scoped_download_folder")),
                RequirePinVerification = reader.GetInt32(reader.GetOrdinal("require_pin_verification")) == 1,
                RevokedAt = string.IsNullOrEmpty(revokedStr) ? null : DateTime.Parse(revokedStr)
            };
        }

        return DevicePermissions.Default(deviceId);
    }

    public async Task UpdatePermissionsAsync(DevicePermissions permissions, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO device_permissions (device_id, allow_auto_accept, max_auto_accept_bytes, scoped_download_folder, require_pin_verification, revoked_at)
            VALUES (@id, @auto, @max_bytes, @folder, @pin, @revoked)
            ON CONFLICT(device_id) DO UPDATE SET
                allow_auto_accept = excluded.allow_auto_accept,
                max_auto_accept_bytes = excluded.max_auto_accept_bytes,
                scoped_download_folder = excluded.scoped_download_folder,
                require_pin_verification = excluded.require_pin_verification,
                revoked_at = excluded.revoked_at;
        """;

        cmd.Parameters.AddWithValue("@id", permissions.DeviceId);
        cmd.Parameters.AddWithValue("@auto", permissions.AllowAutoAccept ? 1 : 0);
        cmd.Parameters.AddWithValue("@max_bytes", permissions.MaxAutoAcceptBytes);
        cmd.Parameters.AddWithValue("@folder", (object?)permissions.ScopedDownloadFolder ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pin", permissions.RequirePinVerification ? 1 : 0);
        cmd.Parameters.AddWithValue("@revoked", permissions.RevokedAt.HasValue ? permissions.RevokedAt.Value.ToString("O") : (object)DBNull.Value);

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

