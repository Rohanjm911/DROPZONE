using System.Runtime.InteropServices;
using System.Text.Json;
using DropZone.Domain.Interfaces;
using DropZone.Domain.Models;

namespace DropZone.Engine.Interop;

public sealed class DropZoneEngineInterop : IDropZoneEngine
{
    private NativeMethods.EventCallback? _callbackDelegate;
    private GCHandle _callbackHandle;
    private bool _initialized;

    public event EventHandler<EngineEvent>? EventReceived;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public Task InitializeAsync(EngineConfig config, CancellationToken ct = default)
    {
        var configJson = JsonSerializer.Serialize(config, JsonOptions);
        var res = NativeMethods.DropZoneEngineInit(configJson);
        if (res != 0)
        {
            var err = GetLastError();
            throw new InvalidOperationException($"Engine initialization failed with code {res}: {err}");
        }

        // Pin the callback delegate
        _callbackDelegate = OnNativeEvent;
        _callbackHandle = GCHandle.Alloc(_callbackDelegate);
        NativeMethods.DropZoneEngineSetEventCallback(_callbackDelegate, IntPtr.Zero);

        _initialized = true;
        return Task.CompletedTask;
    }

    private void OnNativeEvent(int eventType, IntPtr payloadJsonUtf8, IntPtr userData)
    {
        var json = payloadJsonUtf8 != IntPtr.Zero 
            ? Marshal.PtrToStringUTF8(payloadJsonUtf8) ?? "{}" 
            : "{}";

        EventReceived?.Invoke(this, new EngineEvent
        {
            EventType = eventType,
            PayloadJson = json
        });
    }

    public Task StartServerAsync(string bindAddress, ushort port, CancellationToken ct = default)
    {
        EnsureInitialized();
        var res = NativeMethods.DropZoneEngineStartServer(bindAddress, port);
        if (res != 0)
            throw new InvalidOperationException($"Failed to start server: {GetLastError()}");
        return Task.CompletedTask;
    }

    public Task StopServerAsync(CancellationToken ct = default)
    {
        NativeMethods.DropZoneEngineStopServer();
        return Task.CompletedTask;
    }

    public Task StartDiscoveryAsync(string deviceName, ushort port, CancellationToken ct = default)
    {
        EnsureInitialized();
        var res = NativeMethods.DropZoneEngineStartDiscovery(deviceName, port);
        if (res != 0)
            throw new InvalidOperationException($"Failed to start discovery: {GetLastError()}");
        return Task.CompletedTask;
    }

    public Task StopDiscoveryAsync(CancellationToken ct = default)
    {
        NativeMethods.DropZoneEngineStopDiscovery();
        return Task.CompletedTask;
    }

    public Task SendJobAsync(string peerIp, ushort peerPort, TransferManifest manifest, CancellationToken ct = default)
    {
        EnsureInitialized();
        var manifestJson = JsonSerializer.Serialize(manifest);
        var res = NativeMethods.DropZoneEngineSendJob(peerIp, peerPort, manifestJson);
        if (res != 0)
            throw new InvalidOperationException($"Failed to send job: {GetLastError()}");
        return Task.CompletedTask;
    }

    public Task AcceptJobAsync(Guid jobId, ulong resumeOffset, CancellationToken ct = default)
    {
        EnsureInitialized();
        var res = NativeMethods.DropZoneEngineAcceptJob(jobId.ToString(), resumeOffset);
        if (res != 0)
            throw new InvalidOperationException($"Failed to accept job: {GetLastError()}");
        return Task.CompletedTask;
    }

    public Task RejectJobAsync(Guid jobId, string reason, CancellationToken ct = default)
    {
        EnsureInitialized();
        var res = NativeMethods.DropZoneEngineRejectJob(jobId.ToString(), reason);
        if (res != 0)
            throw new InvalidOperationException($"Failed to reject job: {GetLastError()}");
        return Task.CompletedTask;
    }

    public Task PauseJobAsync(Guid jobId, CancellationToken ct = default)
    {
        EnsureInitialized();
        var res = NativeMethods.DropZoneEnginePauseJob(jobId.ToString());
        if (res != 0)
            throw new InvalidOperationException($"Failed to pause job: {GetLastError()}");
        return Task.CompletedTask;
    }

    public Task ResumeJobAsync(Guid jobId, CancellationToken ct = default)
    {
        EnsureInitialized();
        var res = NativeMethods.DropZoneEngineResumeJob(jobId.ToString());
        if (res != 0)
            throw new InvalidOperationException($"Failed to resume job: {GetLastError()}");
        return Task.CompletedTask;
    }

    public Task CancelJobAsync(Guid jobId, CancellationToken ct = default)
    {
        EnsureInitialized();
        var res = NativeMethods.DropZoneEngineCancelJob(jobId.ToString());
        if (res != 0)
            throw new InvalidOperationException($"Failed to cancel job: {GetLastError()}");
        return Task.CompletedTask;
    }

    public Task<IdentityKeyPair> GenerateIdentityKeyAsync(CancellationToken ct = default)
    {
        EnsureInitialized();
        var buffer = Marshal.AllocHGlobal(1024);
        try
        {
            var res = NativeMethods.DropZoneEngineGenerateIdentityKey(buffer, 1024);
            if (res != 0)
                throw new InvalidOperationException($"Failed to generate identity key (code {res}): {GetLastError()}");

            var json = Marshal.PtrToStringUTF8(buffer) ?? "{}";
            var keyPair = JsonSerializer.Deserialize<IdentityKeyPair>(json, JsonOptions)
                ?? throw new InvalidOperationException("Failed to parse identity key JSON");
            return Task.FromResult(keyPair);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public Task StartPairingAsync(string peerIp, ushort peerPort, CancellationToken ct = default)
    {
        EnsureInitialized();
        var res = NativeMethods.DropZoneEngineStartPairing(peerIp, peerPort);
        if (res != 0)
            throw new InvalidOperationException($"Failed to start pairing (code {res}): {GetLastError()}");
        return Task.CompletedTask;
    }

    public Task<bool> ConfirmPairingAsync(string peerDeviceId, string pinCode, bool isAccepted, CancellationToken ct = default)
    {
        EnsureInitialized();
        var res = NativeMethods.DropZoneEngineConfirmPairing(peerDeviceId, pinCode, isAccepted);
        return Task.FromResult(res == 0);
    }

    public Task<NatCandidate?> TestStunAsync(string stunServer, CancellationToken ct = default)
    {
        EnsureInitialized();
        var buffer = Marshal.AllocHGlobal(2048);
        try
        {
            var res = NativeMethods.DropZoneEngineTestStun(stunServer, buffer, 2048);
            if (res != 0)
                return Task.FromResult<NatCandidate?>(null);

            var json = Marshal.PtrToStringUTF8(buffer);
            if (string.IsNullOrWhiteSpace(json))
                return Task.FromResult<NatCandidate?>(null);

            var candidate = JsonSerializer.Deserialize<NatCandidate>(json, JsonOptions);
            return Task.FromResult(candidate);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public Task GatherNatCandidatesAsync(string stunServer, CancellationToken ct = default)
    {
        EnsureInitialized();
        var res = NativeMethods.DropZoneEngineGatherNatCandidates(stunServer);
        if (res != 0)
            throw new InvalidOperationException($"Failed to gather NAT candidates: {GetLastError()}");
        return Task.CompletedTask;
    }

    private void EnsureInitialized()
    {
        if (!_initialized)
            throw new InvalidOperationException("DropZoneEngine is not initialized.");
    }

    private static string GetLastError()
    {
        var ptr = NativeMethods.DropZoneEngineGetLastError();
        return ptr != IntPtr.Zero ? Marshal.PtrToStringUTF8(ptr) ?? "Unknown native error" : "Unknown native error";
    }

    public ValueTask DisposeAsync()
    {
        if (_initialized)
        {
            NativeMethods.DropZoneEngineShutdown();
            if (_callbackHandle.IsAllocated)
            {
                _callbackHandle.Free();
            }
            _initialized = false;
        }
        return ValueTask.CompletedTask;
    }
}
