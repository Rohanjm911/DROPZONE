using DropZone.Domain.Interfaces;
using DropZone.Engine.Interop;
using Xunit;

namespace DropZone.UnitTests;

public class EngineInteropTests
{
    [Fact]
    public async Task Engine_Lifecycle_InitializesAndHandlesEvents()
    {
        var engine = new DropZoneEngineInterop();
        var serverStartedTcs = new TaskCompletionSource<bool>();
        var pausedTcs = new TaskCompletionSource<bool>();
        var resumedTcs = new TaskCompletionSource<bool>();
        var cancelledTcs = new TaskCompletionSource<bool>();

        engine.EventReceived += (_, e) =>
        {
            if (e.EventType == 1) // DZ_EVENT_SERVER_STARTED
            {
                serverStartedTcs.TrySetResult(true);
            }
            else if (e.EventType == 30) // DZ_EVENT_PAUSED
            {
                pausedTcs.TrySetResult(true);
            }
            else if (e.EventType == 31) // DZ_EVENT_RESUMED
            {
                resumedTcs.TrySetResult(true);
            }
            else if (e.EventType == 32) // DZ_EVENT_CANCELLED
            {
                cancelledTcs.TrySetResult(true);
            }
        };

        var config = new EngineConfig
        {
            DeviceId = "test-device-uuid",
            DeviceName = "Test Runner Node",
            DownloadDirectory = Path.GetTempPath(),
            ListenPort = 0 // OS assigned port
        };

        // 1. Initialize
        await engine.InitializeAsync(config);

        // 2. Start server
        await engine.StartServerAsync("127.0.0.1", 0);
        var serverStarted = await Task.WhenAny(serverStartedTcs.Task, Task.Delay(2000));
        Assert.True(serverStarted == serverStartedTcs.Task, "Server started event timed out");

        // 3. Pause Job
        var testJobId = Guid.NewGuid();
        await engine.PauseJobAsync(testJobId);
        var paused = await Task.WhenAny(pausedTcs.Task, Task.Delay(2000));
        Assert.True(paused == pausedTcs.Task, "Pause event timed out");

        // 4. Resume Job
        await engine.ResumeJobAsync(testJobId);
        var resumed = await Task.WhenAny(resumedTcs.Task, Task.Delay(2000));
        Assert.True(resumed == resumedTcs.Task, "Resume event timed out");

        // 5. Cancel Job
        await engine.CancelJobAsync(testJobId);
        var cancelled = await Task.WhenAny(cancelledTcs.Task, Task.Delay(2000));
        Assert.True(cancelled == cancelledTcs.Task, "Cancel event timed out");

        // 6. Stop server and dispose
        await engine.StopServerAsync();
        await engine.DisposeAsync();
    }

    [Fact]
    public async Task Engine_GenerateIdentityKey_ReturnsValidCurve25519Key()
    {
        var engine = new DropZoneEngineInterop();
        var config = new EngineConfig
        {
            DeviceId = "test-crypto-uuid",
            DeviceName = "Crypto Node",
            DownloadDirectory = Path.GetTempPath(),
            ListenPort = 0
        };
        await engine.InitializeAsync(config);

        var keyPair = await engine.GenerateIdentityKeyAsync();
        Assert.NotNull(keyPair);
        Assert.Equal(64, keyPair.PublicKeyHex.Length);
        Assert.Equal(64, keyPair.Fingerprint.Length);

        await engine.DisposeAsync();
    }
}

