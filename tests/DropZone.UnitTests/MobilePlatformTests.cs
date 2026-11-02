using DropZone.Android;
using DropZone.Application.Services;
using DropZone.Domain.Enums;
using DropZone.Domain.Interfaces;
using DropZone.Domain.Models;
using DropZone.Infrastructure.Platform;
using DropZone.iOS;
using Xunit;

namespace DropZone.UnitTests;

public class MobilePlatformTests
{
    [Fact]
    public void AndroidPlatformService_ReportsCorrectPlatformAndIsMobile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "dz_android_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new AndroidPlatformService(tempDir, "Pixel 8 Pro", "Android 15");

            Assert.Equal(PlatformType.Android, service.Platform);
            Assert.True(service.IsMobile);
            Assert.Equal("Pixel 8 Pro", service.DeviceModel);
            Assert.Equal("Android 15", service.OperatingSystemVersion);

            var downloadDir = service.GetStorageDirectory(StorageDirectoryType.Downloads);
            Assert.True(Directory.Exists(downloadDir));
            Assert.Contains("Downloads", downloadDir);

            var quarantineDir = service.GetStorageDirectory(StorageDirectoryType.Quarantine);
            Assert.True(Directory.Exists(quarantineDir));
            Assert.Contains("Quarantine", quarantineDir);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
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

    [Fact]
    public async Task AndroidWakeLockService_AcquiresAndReleasesLocksProperly()
    {
        var wakeLock = new AndroidWakeLockService();

        Assert.False(wakeLock.IsCpuWakeLockHeld);
        Assert.Empty(wakeLock.ActiveLocks);

        var lock1 = "transfer-job-1";
        await wakeLock.AcquireWakeLockAsync(lock1, "Streaming chunk batch 1");

        Assert.True(wakeLock.IsLockActive(lock1));
        Assert.True(wakeLock.IsCpuWakeLockHeld);
        Assert.Single(wakeLock.ActiveLocks);

        var lock2 = "transfer-job-2";
        await wakeLock.AcquireWakeLockAsync(lock2, "Streaming chunk batch 2");
        Assert.Equal(2, wakeLock.ActiveLocks.Count);

        await wakeLock.ReleaseWakeLockAsync(lock1);
        Assert.False(wakeLock.IsLockActive(lock1));
        Assert.True(wakeLock.IsLockActive(lock2));
        Assert.True(wakeLock.IsCpuWakeLockHeld);

        await wakeLock.ReleaseWakeLockAsync(lock2);
        Assert.False(wakeLock.IsLockActive(lock2));
        Assert.False(wakeLock.IsCpuWakeLockHeld);
        Assert.Empty(wakeLock.ActiveLocks);
    }

    [Fact]
    public void AndroidWakeLockService_ControlsMulticastLockForDiscovery()
    {
        var wakeLock = new AndroidWakeLockService();

        Assert.False(wakeLock.IsMulticastLockHeld);

        wakeLock.AcquireMulticastLock();
        Assert.True(wakeLock.IsMulticastLockHeld);

        wakeLock.ReleaseMulticastLock();
        Assert.False(wakeLock.IsMulticastLockHeld);
    }

    [Fact]
    public async Task AndroidNotificationService_TracksProgressAndCompletion()
    {
        var notifService = new AndroidNotificationService();
        var jobId = Guid.NewGuid().ToString();

        // 1. Progress notification
        await notifService.ShowTransferProgressNotificationAsync(
            jobId,
            "sample_video.mp4",
            45,
            2_500_000,
            12,
            isOngoing: true);

        Assert.Single(notifService.ActiveNotifications);
        var active = notifService.ActiveNotifications.First();
        Assert.Equal(jobId, active.Id);
        Assert.Equal(45, active.Progress);
        Assert.True(active.IsOngoing);
        Assert.Contains("2.5 MB/s", active.Text);
        Assert.Contains("12s left", active.Text);

        // 2. Completed notification
        await notifService.ShowTransferCompletedNotificationAsync(
            jobId,
            "sample_video.mp4",
            50_000_000,
            isSuccess: true);

        // Active ongoing notification was cleaned up and replaced by completed
        var completed = notifService.ActiveNotifications.FirstOrDefault(n => n.Id == jobId);
        Assert.Null(completed); // Removed from active dictionary

        // 3. Security alert
        await notifService.ShowSecurityAlertNotificationAsync(
            "Security Warning",
            "Disguised executable blocked",
            "payload.pdf.exe");

        var alert = notifService.ActiveNotifications.FirstOrDefault(n => n.ChannelId == AndroidNotificationService.ChannelAlerts);
        Assert.NotNull(alert);
        Assert.Contains("Disguised executable", alert.Text);
    }

    [Fact]
    public void AndroidForegroundTransferService_CoordinatesWithWakeLockAndMulticast()
    {
        var wakeLock = new AndroidWakeLockService();
        var notifService = new AndroidNotificationService();
        var fgService = new DropZoneTransferForegroundService(wakeLock, notifService);

        Assert.False(fgService.IsServiceRunning);
        Assert.Equal(0, fgService.ActiveTransfersCount);

        // Transfer 1 starts
        fgService.OnTransferStarted("job-1", "large_archive.zip");
        Assert.True(fgService.IsServiceRunning);
        Assert.Equal(1, fgService.ActiveTransfersCount);
        Assert.True(wakeLock.IsMulticastLockHeld);
        Assert.True(wakeLock.IsLockActive("job-1"));

        // Transfer 2 starts concurrently
        fgService.OnTransferStarted("job-2", "photo_album.tar");
        Assert.Equal(2, fgService.ActiveTransfersCount);
        Assert.True(fgService.IsServiceRunning);

        // Transfer 1 finishes
        fgService.OnTransferEnded("job-1");
        Assert.Equal(1, fgService.ActiveTransfersCount);
        Assert.True(fgService.IsServiceRunning);
        Assert.False(wakeLock.IsLockActive("job-1"));
        Assert.True(wakeLock.IsLockActive("job-2"));

        // Transfer 2 finishes
        fgService.OnTransferEnded("job-2");
        Assert.Equal(0, fgService.ActiveTransfersCount);
        Assert.False(fgService.IsServiceRunning);
        Assert.False(wakeLock.IsMulticastLockHeld);
        Assert.False(wakeLock.IsCpuWakeLockHeld);
    }

    [Fact]
    public void IOSPlatformService_ReportsCorrectPlatformAndIsMobile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "dz_ios_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var service = new IOSPlatformService(tempDir, "iPhone 16 Pro", "iOS 18.1");

            Assert.Equal(PlatformType.IOS, service.Platform);
            Assert.True(service.IsMobile);
            Assert.Equal("iPhone 16 Pro", service.DeviceModel);
            Assert.Equal("iOS 18.1", service.OperatingSystemVersion);

            var downloads = service.GetStorageDirectory(StorageDirectoryType.Downloads);
            Assert.True(Directory.Exists(downloads));
            Assert.Contains("Downloads", downloads);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
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

    [Fact]
    public async Task IOSWakeLockService_TogglesIdleTimerDisabledOnTransfers()
    {
        var wakeLock = new IOSWakeLockService();

        Assert.False(wakeLock.IsIdleTimerDisabled);

        await wakeLock.AcquireWakeLockAsync("ios-job-1", "Sending photos");
        Assert.True(wakeLock.IsIdleTimerDisabled);

        await wakeLock.AcquireWakeLockAsync("ios-job-2", "Sending videos");
        Assert.True(wakeLock.IsIdleTimerDisabled);

        await wakeLock.ReleaseWakeLockAsync("ios-job-1");
        Assert.True(wakeLock.IsIdleTimerDisabled);

        await wakeLock.ReleaseWakeLockAsync("ios-job-2");
        Assert.False(wakeLock.IsIdleTimerDisabled);
    }

    [Fact]
    public void IOSBackgroundTransferManager_TracksActiveTransfers()
    {
        var wakeLock = new IOSWakeLockService();
        var notifService = new IOSNotificationService();
        var manager = new DropZoneBackgroundTransferManager(wakeLock, notifService);

        Assert.False(manager.IsBackgroundProcessingActive);
        Assert.Equal(0, manager.ActiveTransfersCount);

        manager.OnTransferStarted("ios-job-10", "document.pdf");
        Assert.True(manager.IsBackgroundProcessingActive);
        Assert.Equal(1, manager.ActiveTransfersCount);
        Assert.True(wakeLock.IsLockActive("ios-job-10"));

        manager.OnTransferEnded("ios-job-10");
        Assert.False(manager.IsBackgroundProcessingActive);
        Assert.Equal(0, manager.ActiveTransfersCount);
        Assert.False(wakeLock.IsLockActive("ios-job-10"));
    }

    [Fact]
    public async Task DesktopPlatformServices_CorrectlyResolvesPlatformAndLocks()
    {
        var platform = new DesktopPlatformService();
        Assert.False(platform.IsMobile);
        Assert.True(platform.Platform is PlatformType.Windows or PlatformType.Linux or PlatformType.MacOS);

        var wakeLock = new DesktopWakeLockService();
        await wakeLock.AcquireWakeLockAsync("desktop-1", "Local LAN file copy");
        Assert.True(wakeLock.IsLockActive("desktop-1"));
        await wakeLock.ReleaseWakeLockAsync("desktop-1");
        Assert.False(wakeLock.IsLockActive("desktop-1"));

        var notif = new DesktopNotificationService();
        var notifFired = false;
        notif.NotificationPosted += (_, _) => notifFired = true;

        await notif.ShowTransferProgressNotificationAsync("d1", "test.bin", 50, 1000000, 5);
        Assert.True(notifFired);

        var lifecycle = new DesktopLifecycleService();
        Assert.Equal(AppLifecycleState.Foreground, lifecycle.CurrentState);

        await lifecycle.NotifyBackgroundTransferStartingAsync("bg-1");
        Assert.Equal(1, lifecycle.ActiveBackgroundTransfersCount);

        await lifecycle.NotifyBackgroundTransferFinishedAsync("bg-1");
        Assert.Equal(0, lifecycle.ActiveBackgroundTransfersCount);
    }

    [Fact]
    public async Task TransferOrchestrator_CoordinatesMobilePlatformWakeLockAndNotifications()
    {
        var mockEngine = new MockDropZoneEngine();
        var tempDir = Path.Combine(Path.GetTempPath(), "dz_orch_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var dbPath = Path.Combine(tempDir, "test.db");
            var transferRepo = new DropZone.Infrastructure.Database.SqliteTransferRepository(dbPath);
            var deviceRepo = new DropZone.Infrastructure.Database.SqliteDeviceRepository(dbPath);
            var sec = new DropZone.Infrastructure.Storage.SecurityService();
            var storage = new DropZone.Infrastructure.Storage.LocalFileSystemStorage(tempDir, sec);
            var queue = new TransferQueue();

            var wakeLock = new AndroidWakeLockService();
            var notif = new AndroidNotificationService();
            var lifecycle = new DesktopLifecycleService();

            var orchestrator = new TransferOrchestrator(
                mockEngine,
                transferRepo,
                deviceRepo,
                storage,
                sec,
                queue,
                notif,
                wakeLock,
                lifecycle);

            await orchestrator.InitializeAsync(new EngineConfig
            {
                DeviceId = "local-device",
                DeviceName = "Test-Device",
                DownloadDirectory = tempDir,
                ListenPort = 52490
            });

            // 1. Create a dummy file to send
            var sourceFile = Path.Combine(tempDir, "file_to_send.txt");
            await File.WriteAllTextAsync(sourceFile, "Hello DropZone Cross-Platform Mobile Test!");

            var targetDevice = new Device
            {
                Id = "remote-phone-id",
                Name = "Pixel 8",
                IpAddress = "192.168.1.100",
                Port = 52490,
                TrustLevel = DeviceTrustLevel.Trusted
            };

            // 2. Start outbound transfer
            var job = await orchestrator.StartOutboundTransferAsync(
                sourceFile,
                targetDevice,
                "local-device",
                "Test-Device");

            // Wake lock and lifecycle background transfer should be active!
            Assert.True(wakeLock.IsLockActive(job.Id.ToString()));
            Assert.Equal(1, lifecycle.ActiveBackgroundTransfersCount);

            // 3. Simulate progress event from engine
            mockEngine.RaiseProgressEvent(job.Id, 20, 42);
            await Task.Delay(50); // Allow async dispatch

            Assert.Contains(notif.ActiveNotifications, n => n.Id == job.Id.ToString());

            // 4. Simulate completed event from engine
            mockEngine.RaiseCompletedEvent(job.Id);
            await Task.Delay(50);

            // Wake lock should be released and lifecycle active count 0
            Assert.False(wakeLock.IsLockActive(job.Id.ToString()));
            Assert.Equal(0, lifecycle.ActiveBackgroundTransfersCount);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
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

public sealed class MockDropZoneEngine : IDropZoneEngine
{
    public event EventHandler<EngineEvent>? EventReceived;

    public Task InitializeAsync(EngineConfig config, CancellationToken ct = default) => Task.CompletedTask;
    public Task StartServerAsync(string bindAddress, ushort port, CancellationToken ct = default) => Task.CompletedTask;
    public Task StopServerAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task StartDiscoveryAsync(string deviceName, ushort port, CancellationToken ct = default) => Task.CompletedTask;
    public Task StopDiscoveryAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task SendJobAsync(string peerIp, ushort peerPort, TransferManifest manifest, CancellationToken ct = default) => Task.CompletedTask;
    public Task AcceptJobAsync(Guid jobId, ulong resumeOffset, CancellationToken ct = default) => Task.CompletedTask;
    public Task RejectJobAsync(Guid jobId, string reason, CancellationToken ct = default) => Task.CompletedTask;
    public Task PauseJobAsync(Guid jobId, CancellationToken ct = default) => Task.CompletedTask;
    public Task ResumeJobAsync(Guid jobId, CancellationToken ct = default) => Task.CompletedTask;
    public Task CancelJobAsync(Guid jobId, CancellationToken ct = default) => Task.CompletedTask;
    public Task<IdentityKeyPair> GenerateIdentityKeyAsync(CancellationToken ct = default) => Task.FromResult(new IdentityKeyPair());
    public Task StartPairingAsync(string peerIp, ushort peerPort, CancellationToken ct = default) => Task.CompletedTask;
    public Task<bool> ConfirmPairingAsync(string peerDeviceId, string pinCode, bool isAccepted, CancellationToken ct = default) => Task.FromResult(true);
    public Task<NatCandidate?> TestStunAsync(string stunServer, CancellationToken ct = default) => Task.FromResult<NatCandidate?>(null);
    public Task GatherNatCandidatesAsync(string stunServer, CancellationToken ct = default) => Task.CompletedTask;
    public Task PingPeerAsync(string peerIp, ushort peerPort, CancellationToken ct = default) => Task.CompletedTask;

    public void RaiseProgressEvent(Guid jobId, long transferred, long total)
    {
        var json = $"{{\"job_id\":\"{jobId}\",\"bytes_transferred\":{transferred},\"total_bytes\":{total}}}";
        EventReceived?.Invoke(this, new EngineEvent { EventType = 20, PayloadJson = json });
    }

    public void RaiseCompletedEvent(Guid jobId)
    {
        var json = $"{{\"job_id\":\"{jobId}\"}}";
        EventReceived?.Invoke(this, new EngineEvent { EventType = 40, PayloadJson = json });
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
