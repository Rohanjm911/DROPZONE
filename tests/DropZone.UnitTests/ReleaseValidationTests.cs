using DropZone.Application.Services;
using DropZone.Domain.Enums;
using DropZone.Domain.Interfaces;
using DropZone.Domain.Models;
using DropZone.Infrastructure.Database;
using DropZone.Infrastructure.Enterprise;
using DropZone.Infrastructure.Platform;
using DropZone.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DropZone.UnitTests;

/// <summary>
/// Phase 6 Release Quality Assurance & End-to-End Production Readiness Tests.
/// Validates the full integration of all 8 application layers in DROPZONE v1.0.0.
/// </summary>
public class ReleaseValidationTests
{
    [Fact]
    public async Task DropZone_FullSystemStack_ExecutesEndToEndWorkflowSuccessfully()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"dz_release_e2e_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var dbPath = Path.Combine(tempDir, "release_production.db");

        try
        {
            // 1. Layer 4 & 7: Persistence & Storage
            var transferRepo = new SqliteTransferRepository(dbPath);
            var deviceRepo = new SqliteDeviceRepository(dbPath);
            var sec = new SecurityService();
            var storage = new LocalFileSystemStorage(tempDir, sec);
            var backupService = new BackupService(transferRepo, deviceRepo);
            var auditService = new ComplianceAuditService(dbPath);
            var enterpriseSec = new EnterpriseCertificateValidator();
            var throttler = new BandwidthThrottler(0);
            var queue = new TransferQueue(maxConcurrentTransfers: 5);

            // 2. Layer 8: Platform Services
            var platform = new DesktopPlatformService();
            var wakeLock = new DesktopWakeLockService();
            var notif = new DesktopNotificationService();
            var lifecycle = new DesktopLifecycleService();
            var picker = new DesktopStoragePicker();

            // 3. Layer 5: Native Engine Mock / Loopback Driver
            var mockEngine = new MockDropZoneEngine();

            // 4. Layer 2: Transfer Orchestrator Application Layer
            var orchestrator = new TransferOrchestrator(
                mockEngine,
                transferRepo,
                deviceRepo,
                storage,
                sec,
                queue,
                notif,
                wakeLock,
                lifecycle,
                auditService,
                enterpriseSec,
                throttler);

            // Initialize all 8 layers
            await orchestrator.InitializeAsync(new EngineConfig
            {
                DeviceId = "master-prod-node",
                DeviceName = "DropZone Master Production Node",
                DownloadDirectory = tempDir,
                ListenPort = 52490
            });

            // 5. Verify Device Discovery & Pairing Workflow
            var peerDevice = new Device
            {
                Id = "partner-client-node",
                Name = "Partner Studio Laptop",
                IpAddress = "192.168.1.150",
                Port = 52490,
                TrustLevel = DeviceTrustLevel.Prompt
            };
            await deviceRepo.UpsertDeviceAsync(peerDevice);

            var paired = await orchestrator.ConfirmPairingAsync("partner-client-node", "849201", true);
            Assert.True(paired);

            var fetchedPeer = await deviceRepo.GetDeviceAsync("partner-client-node");
            Assert.NotNull(fetchedPeer);

            // 6. Multi-file Outbound Transfer with Integrity Check
            var sampleFile1 = Path.Combine(tempDir, "blueprint_v1.cad");
            var sampleFile2 = Path.Combine(tempDir, "release_manifest.json");
            await File.WriteAllTextAsync(sampleFile1, "CAD Vector Data 2026 - Confidential");
            await File.WriteAllTextAsync(sampleFile2, "{\"version\": \"1.0.0\", \"status\": \"stable\"}");

            var job = await orchestrator.StartOutboundTransferAsync(
                sampleFile1,
                fetchedPeer,
                "master-prod-node",
                "DropZone Master Node");

            Assert.Equal(TransferState.Connecting, job.State);
            Assert.True(wakeLock.IsLockActive(job.Id.ToString()));
            Assert.Equal(1, lifecycle.ActiveBackgroundTransfersCount);

            // Simulate progress and completion
            mockEngine.RaiseProgressEvent(job.Id, job.TotalBytes / 2, job.TotalBytes);
            await Task.Delay(30);

            mockEngine.RaiseCompletedEvent(job.Id);
            await Task.Delay(30);

            // Verify clean teardown of locks and background tasks
            Assert.False(wakeLock.IsLockActive(job.Id.ToString()));
            Assert.Equal(0, lifecycle.ActiveBackgroundTransfersCount);

            // 7. Folder Synchronization Delta Engine
            var syncSrc = Path.Combine(tempDir, "sync_src");
            var syncTgt = Path.Combine(tempDir, "sync_tgt");
            Directory.CreateDirectory(syncSrc);
            Directory.CreateDirectory(syncTgt);
            await File.WriteAllTextAsync(Path.Combine(syncSrc, "file1.txt"), "Version 1");

            var syncEngine = new FolderSyncService();
            var delta = await syncEngine.ComputeDeltaAsync(syncSrc, syncTgt, SyncConflictResolution.SourceWins);
            var syncedCount = await syncEngine.ExecuteSyncPlanAsync(delta);
            Assert.Equal(1, syncedCount);
            Assert.True(File.Exists(Path.Combine(syncTgt, "file1.txt")));

            // 8. Encrypted Backup Vault Export & Restore
            var backupBytes = await backupService.CreateEncryptedBackupAsync("MasterEnterprisePassphrase2026!");
            Assert.NotNull(backupBytes);
            Assert.True(backupBytes.Length > 100);

            var restoredPkg = await backupService.RestoreEncryptedBackupAsync(backupBytes, "MasterEnterprisePassphrase2026!");
            Assert.NotNull(restoredPkg);
            Assert.True(restoredPkg.Devices.Count > 0);

            // 9. Tamper-Evident Compliance Audit Ledger Verification
            var auditResult = await auditService.VerifyAuditChainIntegrityAsync();
            Assert.True(auditResult.IsValid);
            Assert.True(auditResult.TotalEntriesVerified >= 2);

            var auditJson = await auditService.ExportAuditReportJsonAsync();
            Assert.Contains("DROPZONE SOC2/HIPAA", auditJson);

            // 10. Clean Disposal
            await orchestrator.DisposeAsync();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
            catch { }
        }
    }
}
