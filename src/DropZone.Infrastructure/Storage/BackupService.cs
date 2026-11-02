using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DropZone.Domain.Interfaces;
using DropZone.Domain.Models;

namespace DropZone.Infrastructure.Storage;

public sealed class BackupService : IBackupService
{
    private readonly ITransferRepository _transferRepo;
    private readonly IDeviceRepository _deviceRepo;

    private static readonly byte[] MagicHeader = "DZBK1"u8.ToArray();
    private const int SaltSizeBytes = 16;
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;
    private const int Pbkdf2Iterations = 100_000;

    public BackupService(ITransferRepository transferRepo, IDeviceRepository deviceRepo)
    {
        _transferRepo = transferRepo;
        _deviceRepo = deviceRepo;
    }

    public async Task<byte[]> CreateEncryptedBackupAsync(string passphrase, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(passphrase))
            throw new ArgumentException("Passphrase cannot be empty for encrypted backup.", nameof(passphrase));

        var devices = await _deviceRepo.GetAllDevicesAsync(ct);
        var jobs = await _transferRepo.GetAllJobsAsync(500, ct);

        var package = new DropZoneBackupPackage
        {
            Version = 1,
            ExportedAt = DateTime.UtcNow,
            LocalDeviceId = Environment.MachineName,
            Devices = devices.ToList(),
            TransferHistory = jobs.ToList(),
            Settings = new Dictionary<string, string>
            {
                ["Platform"] = Environment.OSVersion.ToString(),
                ["MachineName"] = Environment.MachineName
            }
        };

        var json = JsonSerializer.Serialize(package, new JsonSerializerOptions { WriteIndented = false });
        var plaintextBytes = Encoding.UTF8.GetBytes(json);

        // Derive key
        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var key = Rfc2898DeriveBytes.Pbkdf2(
            passphrase,
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256,
            32);

        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var tag = new byte[TagSizeBytes];
        var ciphertext = new byte[plaintextBytes.Length];

        using (var aesGcm = new AesGcm(key, TagSizeBytes))
        {
            aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag);
        }

        // Pack: Magic(5) + Salt(16) + Nonce(12) + Tag(16) + Ciphertext(N)
        var totalLength = MagicHeader.Length + SaltSizeBytes + NonceSizeBytes + TagSizeBytes + ciphertext.Length;
        var result = new byte[totalLength];
        var offset = 0;

        Buffer.BlockCopy(MagicHeader, 0, result, offset, MagicHeader.Length);
        offset += MagicHeader.Length;

        Buffer.BlockCopy(salt, 0, result, offset, SaltSizeBytes);
        offset += SaltSizeBytes;

        Buffer.BlockCopy(nonce, 0, result, offset, NonceSizeBytes);
        offset += NonceSizeBytes;

        Buffer.BlockCopy(tag, 0, result, offset, TagSizeBytes);
        offset += TagSizeBytes;

        Buffer.BlockCopy(ciphertext, 0, result, offset, ciphertext.Length);

        return result;
    }

    public async Task<DropZoneBackupPackage> RestoreEncryptedBackupAsync(byte[] backupBytes, string passphrase, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(passphrase))
            throw new ArgumentException("Passphrase is required to decrypt backup.", nameof(passphrase));

        var minHeaderSize = MagicHeader.Length + SaltSizeBytes + NonceSizeBytes + TagSizeBytes;
        if (backupBytes == null || backupBytes.Length < minHeaderSize)
            throw new InvalidOperationException("Backup package is corrupt or truncated.");

        // Check magic
        for (var i = 0; i < MagicHeader.Length; i++)
        {
            if (backupBytes[i] != MagicHeader[i])
                throw new InvalidOperationException("Invalid backup file format (magic mismatch).");
        }

        var offset = MagicHeader.Length;

        var salt = new byte[SaltSizeBytes];
        Buffer.BlockCopy(backupBytes, offset, salt, 0, SaltSizeBytes);
        offset += SaltSizeBytes;

        var nonce = new byte[NonceSizeBytes];
        Buffer.BlockCopy(backupBytes, offset, nonce, 0, NonceSizeBytes);
        offset += NonceSizeBytes;

        var tag = new byte[TagSizeBytes];
        Buffer.BlockCopy(backupBytes, offset, tag, 0, TagSizeBytes);
        offset += TagSizeBytes;

        var cipherLength = backupBytes.Length - offset;
        var ciphertext = new byte[cipherLength];
        Buffer.BlockCopy(backupBytes, offset, ciphertext, 0, cipherLength);

        // Derive key
        var key = Rfc2898DeriveBytes.Pbkdf2(
            passphrase,
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256,
            32);

        var plaintextBytes = new byte[cipherLength];

        try
        {
            using var aesGcm = new AesGcm(key, TagSizeBytes);
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintextBytes);
        }
        catch (CryptographicException)
        {
            throw new CryptographicException("Authentication tag failed. Invalid passphrase or altered backup file.");
        }

        var json = Encoding.UTF8.GetString(plaintextBytes);
        var package = JsonSerializer.Deserialize<DropZoneBackupPackage>(json)
            ?? throw new InvalidOperationException("Failed to parse backup package payload.");

        // Merge restored devices
        foreach (var dev in package.Devices)
        {
            await _deviceRepo.UpsertDeviceAsync(dev, ct);
        }

        return package;
    }

    public async Task ExportBackupToFileAsync(string filePath, string passphrase, CancellationToken ct = default)
    {
        var bytes = await CreateEncryptedBackupAsync(passphrase, ct);
        await File.WriteAllBytesAsync(filePath, bytes, ct);
    }

    public async Task<DropZoneBackupPackage> ImportBackupFromFileAsync(string filePath, string passphrase, CancellationToken ct = default)
    {
        var bytes = await File.ReadAllBytesAsync(filePath, ct);
        return await RestoreEncryptedBackupAsync(bytes, passphrase, ct);
    }
}

