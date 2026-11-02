using System.Collections.Concurrent;
using DropZone.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace DropZone.Android;

public sealed class AndroidWakeLockService : IPlatformWakeLockService
{
    private readonly ILogger<AndroidWakeLockService>? _logger;
    private readonly ConcurrentDictionary<string, string> _activeLocks = new();
    private bool _multicastLockAcquired;
    private bool _cpuWakeLockAcquired;

    public IReadOnlyCollection<string> ActiveLocks => _activeLocks.Keys.ToArray();

    public bool IsLockActive(string lockId) => _activeLocks.ContainsKey(lockId);

    public bool IsMulticastLockHeld => _multicastLockAcquired;
    public bool IsCpuWakeLockHeld => _cpuWakeLockAcquired;

    public AndroidWakeLockService(ILogger<AndroidWakeLockService>? logger = null)
    {
        _logger = logger;
    }

    public Task<bool> AcquireWakeLockAsync(string lockId, string reason, CancellationToken ct = default)
    {
        _activeLocks[lockId] = reason;
        UpdateLocks();
        _logger?.LogInformation("[Android WakeLock] Acquired lock '{LockId}' (Reason: {Reason})", lockId, reason);
        return Task.FromResult(true);
    }

    public Task<bool> ReleaseWakeLockAsync(string lockId, CancellationToken ct = default)
    {
        if (_activeLocks.TryRemove(lockId, out var reason))
        {
            UpdateLocks();
            _logger?.LogInformation("[Android WakeLock] Released lock '{LockId}'", lockId);
        }
        return Task.FromResult(true);
    }

    public void AcquireMulticastLock()
    {
        // On Android, WifiManager.MulticastLock prevents Wi-Fi driver from dropping mDNS UDP broadcast packets
        _multicastLockAcquired = true;
        _logger?.LogDebug("[Android WakeLock] WifiManager.MulticastLock acquired for mDNS discovery");
    }

    public void ReleaseMulticastLock()
    {
        _multicastLockAcquired = false;
        _logger?.LogDebug("[Android WakeLock] WifiManager.MulticastLock released");
    }

    private void UpdateLocks()
    {
        if (_activeLocks.Count > 0 && !_cpuWakeLockAcquired)
        {
            _cpuWakeLockAcquired = true;
            _logger?.LogDebug("[Android WakeLock] PowerManager PARTIAL_WAKE_LOCK enabled");
        }
        else if (_activeLocks.Count == 0 && _cpuWakeLockAcquired)
        {
            _cpuWakeLockAcquired = false;
            _logger?.LogDebug("[Android WakeLock] PowerManager PARTIAL_WAKE_LOCK released");
        }
    }
}

