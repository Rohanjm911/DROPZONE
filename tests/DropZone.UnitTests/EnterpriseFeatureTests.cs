using DropZone.Application.Services;
using DropZone.Domain.Enums;
using DropZone.Domain.Interfaces;
using DropZone.Domain.Models;
using DropZone.Infrastructure.Database;
using DropZone.Infrastructure.Enterprise;
using DropZone.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DropZone.UnitTests;

public class EnterpriseFeatureTests
{
    [Fact]
    public async Task ComplianceAuditService_LogAndVerifyChain_ReturnsValidIntegrity()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"dz_audit_test_{Guid.NewGuid():N}.db");
        try
        {
            var auditService = new ComplianceAuditService(dbPath);
            await auditService.InitializeAsync();

            // 1. Log sequential events
            var e1 = await auditService.LogEventAsync(AuditEventType.DeviceEnrolled, "node-1", "node-2", "Paired via SAS PIN");
            var e2 = await auditService.LogEventAsync(AuditEventType.TransferStarted, "node-1", "node-2", "Job 100 started (25MB)");
            var e3 = await auditService.LogEventAsync(AuditEventType.TransferCompleted, "node-1", "node-2", "Job 100 completed successfully");
            var e4 = await auditService.LogEventAsync(AuditEventType.SecurityAlertTriggered, "node-3", "node-1", "Blocked suspicious executable");

            Assert.Equal(1, e1.SequenceNumber);
            Assert.Equal(2, e2.SequenceNumber);
            Assert.Equal(3, e3.SequenceNumber);
            Assert.Equal(4, e4.SequenceNumber);

            Assert.Equal(e1.EntryHash, e2.PreviousEntryHash);
            Assert.Equal(e2.EntryHash, e3.PreviousEntryHash);
            Assert.Equal(e3.EntryHash, e4.PreviousEntryHash);

            // 2. Verify entire ledger integrity
            var result = await auditService.VerifyAuditChainIntegrityAsync();
            Assert.True(result.IsValid);
            Assert.Equal(4, result.TotalEntriesVerified);
            Assert.Null(result.BrokenSequenceNumber);

            // 3. Export report
            var json = await auditService.ExportAuditReportJsonAsync();
            Assert.Contains("DROPZONE SOC2/HIPAA", json);
            Assert.Contains("Job 100 completed", json);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public async Task ComplianceAuditService_TamperedRow_DetectsIntegrityFailure()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"dz_audit_tamper_{Guid.NewGuid():N}.db");
        try
        {
            var auditService = new ComplianceAuditService(dbPath);
            await auditService.InitializeAsync();

            await auditService.LogEventAsync(AuditEventType.DeviceEnrolled, "node-1", "node-2", "Event 1");
            await auditService.LogEventAsync(AuditEventType.TransferStarted, "node-1", "node-2", "Event 2");
            await auditService.LogEventAsync(AuditEventType.TransferCompleted, "node-1", "node-2", "Event 3");

            // Tamper directly with the SQLite database row (simulate an attacker modifying audit logs)
            await using (var conn = new SqliteConnection($"Data Source={dbPath}"))
            {
                await conn.OpenAsync();
                await using var cmd = new SqliteCommand("UPDATE compliance_audit_ledger SET details = 'HACKED EVENT' WHERE sequence_num = 2;", conn);
                await cmd.ExecuteNonQueryAsync();
            }

            // Verification should detect the cryptographic mismatch
            var result = await auditService.VerifyAuditChainIntegrityAsync();
            Assert.False(result.IsValid);
            Assert.Equal(2, result.BrokenSequenceNumber);
            Assert.Contains("Tampered entry detected", result.ErrorMessage);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public async Task ComplianceAuditService_DeletedRow_DetectsSequenceGap()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"dz_audit_gap_{Guid.NewGuid():N}.db");
        try
        {
            var auditService = new ComplianceAuditService(dbPath);
            await auditService.InitializeAsync();

            await auditService.LogEventAsync(AuditEventType.DeviceEnrolled, "node-1", "node-2", "Event 1");
            await auditService.LogEventAsync(AuditEventType.TransferStarted, "node-1", "node-2", "Event 2");
            await auditService.LogEventAsync(AuditEventType.TransferCompleted, "node-1", "node-2", "Event 3");

            // Delete entry #2 (simulate an attacker deleting records)
            await using (var conn = new SqliteConnection($"Data Source={dbPath}"))
            {
                await conn.OpenAsync();
                await using var cmd = new SqliteCommand("DELETE FROM compliance_audit_ledger WHERE sequence_num = 2;", conn);
                await cmd.ExecuteNonQueryAsync();
            }

            var result = await auditService.VerifyAuditChainIntegrityAsync();
            Assert.False(result.IsValid);
            Assert.Equal(3, result.BrokenSequenceNumber);
            Assert.Contains("Sequence gap detected", result.ErrorMessage);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public async Task FolderSyncService_ScanDirectory_CollectsHashesAndMetadata()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"dz_sync_scan_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var subDir = Path.Combine(tempDir, "documents", "reports");
            Directory.CreateDirectory(subDir);

            await File.WriteAllTextAsync(Path.Combine(tempDir, "root.txt"), "Root file content");
            await File.WriteAllTextAsync(Path.Combine(subDir, "report2026.pdf"), "Financial summary 2026");

            var syncService = new FolderSyncService();
            var files = await syncService.ScanDirectoryAsync(tempDir);

            Assert.Equal(2, files.Count);
            Assert.Contains(files, f => f.RelativePath == "root.txt" && f.SizeBytes > 0 && !string.IsNullOrEmpty(f.Sha256));
            Assert.Contains(files, f => f.RelativePath == "documents/reports/report2026.pdf" && !string.IsNullOrEmpty(f.Sha256));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task FolderSyncService_ComputeDeltaAndExecuteSyncPlan_WorksCorrectly()
    {
        var srcDir = Path.Combine(Path.GetTempPath(), $"dz_sync_src_{Guid.NewGuid():N}");
        var tgtDir = Path.Combine(Path.GetTempPath(), $"dz_sync_tgt_{Guid.NewGuid():N}");
        Directory.CreateDirectory(srcDir);
        Directory.CreateDirectory(tgtDir);

        try
        {
            // 1. File identical in both
            await File.WriteAllTextAsync(Path.Combine(srcDir, "identical.txt"), "Exact same content");
            await File.WriteAllTextAsync(Path.Combine(tgtDir, "identical.txt"), "Exact same content");

            // 2. File added in source
            await File.WriteAllTextAsync(Path.Combine(srcDir, "new_feature.cs"), "public class NewFeature {}");

            // 3. File modified in source
            await File.WriteAllTextAsync(Path.Combine(srcDir, "config.json"), "{ \"v\": 2 }");
            await File.WriteAllTextAsync(Path.Combine(tgtDir, "config.json"), "{ \"v\": 1 }");

            var syncService = new FolderSyncService();
            var delta = await syncService.ComputeDeltaAsync(srcDir, tgtDir, SyncConflictResolution.SourceWins);

            Assert.Equal(2, delta.TotalChanges);
            Assert.Contains(delta.Differences, d => d.RelativePath == "identical.txt" && d.Status == SyncItemStatus.Identical);
            Assert.Contains(delta.Differences, d => d.RelativePath == "new_feature.cs" && d.Status == SyncItemStatus.Added);
            Assert.Contains(delta.Differences, d => d.RelativePath == "config.json" && d.Status == SyncItemStatus.Modified);

            // Execute sync plan
            var count = await syncService.ExecuteSyncPlanAsync(delta);
            Assert.Equal(2, count);

            // Verify files synced to target
            Assert.True(File.Exists(Path.Combine(tgtDir, "new_feature.cs")));
            var syncedConfig = await File.ReadAllTextAsync(Path.Combine(tgtDir, "config.json"));
            Assert.Equal("{ \"v\": 2 }", syncedConfig);
        }
        finally
        {
            if (Directory.Exists(srcDir)) Directory.Delete(srcDir, true);
            if (Directory.Exists(tgtDir)) Directory.Delete(tgtDir, true);
        }
    }

    [Fact]
    public async Task EnterpriseCertificateValidator_ValidAndRevokedCertificates_EvaluatesProperly()
    {
        var validator = new EnterpriseCertificateValidator();

        var rootCA = new EnterpriseCertificate
        {
            Subject = "CN=DropZone Corporate Root CA, O=Enterprise Inc",
            Issuer = "CN=DropZone Corporate Root CA, O=Enterprise Inc",
            Thumbprint = "ROOTCA1234567890",
            ValidFromUtc = DateTime.UtcNow.AddYears(-2),
            ValidToUtc = DateTime.UtcNow.AddYears(10),
            IsRevoked = false
        };

        await validator.RegisterTrustedRootCertificateAsync(rootCA);

        var validClient = new EnterpriseCertificate
        {
            Subject = "CN=Workstation-01, O=Enterprise Inc",
            Issuer = "CN=DropZone Corporate Root CA, O=Enterprise Inc",
            Thumbprint = "CLIENTVALID111",
            ValidFromUtc = DateTime.UtcNow.AddDays(-30),
            ValidToUtc = DateTime.UtcNow.AddDays(335),
            IsRevoked = false
        };

        var isValid = await validator.ValidateDeviceCertificateAsync("dev-01", validClient);
        Assert.True(isValid);

        // Test expired certificate
        var expiredClient = new EnterpriseCertificate
        {
            Subject = "CN=Old-Workstation, O=Enterprise Inc",
            Issuer = "CN=DropZone Corporate Root CA, O=Enterprise Inc",
            Thumbprint = "EXPIRED999",
            ValidFromUtc = DateTime.UtcNow.AddYears(-3),
            ValidToUtc = DateTime.UtcNow.AddDays(-1),
            IsRevoked = false
        };
        Assert.False(await validator.ValidateDeviceCertificateAsync("dev-02", expiredClient));

        // Test revoked certificate
        await validator.RevokeCertificateAsync("CLIENTVALID111", "Compromised credential");
        Assert.False(await validator.ValidateDeviceCertificateAsync("dev-01", validClient));
    }

    [Fact]
    public async Task BandwidthThrottler_EnforcesRateLimiting()
    {
        var throttler = new BandwidthThrottler(500_000); // 500 KB/s
        Assert.Equal(500_000, throttler.CurrentLimitBytesPerSecond);

        // Unlimited mode (0)
        throttler.SetLimit(0);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await throttler.ThrottleAsync(5_000_000);
        sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 500); // Immediate
    }

    [Fact]
    public async Task TransferOrchestrator_EnterpriseAuditAndPolicyIntegration()
    {
        var mockEngine = new MockDropZoneEngine();
        var tempDir = Path.Combine(Path.GetTempPath(), $"dz_orch_ent_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var dbPath = Path.Combine(tempDir, "ent_test.db");

        try
        {
            var transferRepo = new SqliteTransferRepository(dbPath);
            var deviceRepo = new SqliteDeviceRepository(dbPath);
            var auditService = new ComplianceAuditService(dbPath);
            var enterpriseSec = new EnterpriseCertificateValidator();
            var throttler = new BandwidthThrottler(0);
            var sec = new SecurityService();
            var storage = new LocalFileSystemStorage(tempDir, sec);
            var queue = new TransferQueue();

            var orchestrator = new TransferOrchestrator(
                mockEngine,
                transferRepo,
                deviceRepo,
                storage,
                sec,
                queue,
                auditService: auditService,
                enterpriseSecurityService: enterpriseSec,
                bandwidthThrottler: throttler);

            await orchestrator.InitializeAsync(new EngineConfig
            {
                DeviceId = "ent-local",
                DeviceName = "Enterprise Node",
                DownloadDirectory = tempDir,
                ListenPort = 52490
            });

            // 1. Send outbound transfer
            var file = Path.Combine(tempDir, "classified_data.csv");
            await File.WriteAllTextAsync(file, "id,financial_record\n1,1000000\n");

            var targetDev = new Device
            {
                Id = "partner-node",
                Name = "Partner Workstation",
                IpAddress = "10.0.0.5",
                Port = 52490,
                TrustLevel = DeviceTrustLevel.Trusted
            };

            var job = await orchestrator.StartOutboundTransferAsync(file, targetDev, "ent-local", "Enterprise Node");

            // 2. Complete transfer
            mockEngine.RaiseCompletedEvent(job.Id);
            await Task.Delay(50);

            // 3. Verify audit ledger contains both start and complete
            var auditEntries = await auditService.GetAuditLogAsync();
            Assert.Contains(auditEntries, e => e.EventType == AuditEventType.TransferStarted);
            Assert.Contains(auditEntries, e => e.EventType == AuditEventType.TransferCompleted);

            // 4. Verify hash chain integrity
            var chainResult = await auditService.VerifyAuditChainIntegrityAsync();
            Assert.True(chainResult.IsValid);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }
}
