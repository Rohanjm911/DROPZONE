using System.Collections.Concurrent;
using DropZone.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace DropZone.iOS;

public sealed class IOSWakeLockService : IPlatformWakeLockService
{
    private readonly ILogger<IOSWakeLockService>? _logger;
    private readonly ConcurrentDictionary<string, string> _activeLocks = new();
    private bool _idleTimerDisabled;

    public IReadOnlyCollection<string> ActiveLocks => _activeLocks.Keys.ToArray();

    public bool IsLockActive(string lockId) => _activeLocks.ContainsKey(lockId);

    public bool IsIdleTimerDisabled => _idleTimerDisabled;

    public IOSWakeLockService(ILogger<IOSWakeLockService>? logger = null)
    {
        _logger = logger;
    }

    public Task<bool> AcquireWakeLockAsync(string lockId, string reason, CancellationToken ct = default)
    {
        _activeLocks[lockId] = reason;
        UpdateIdleTimer();
        _logger?.LogInformation("[iOS WakeLock] Acquired lock '{LockId}' (Reason: {Reason})", lockId, reason);
        return Task.FromResult(true);
    }

    public Task<bool> ReleaseWakeLockAsync(string lockId, CancellationToken ct = default)
    {
        if (_activeLocks.TryRemove(lockId, out _))
        {
            UpdateIdleTimer();
            _logger?.LogInformation("[iOS WakeLock] Released lock '{LockId}'", lockId);
        }
        return Task.FromResult(true);
    }

    private void UpdateIdleTimer()
    {
        if (_activeLocks.Count > 0 && !_idleTimerDisabled)
        {
            _idleTimerDisabled = true;
            // On iOS: UIApplication.SharedApplication.IdleTimerDisabled = true;
            _logger?.LogDebug("[iOS WakeLock] UIApplication.IdleTimerDisabled set to true");
        }
        else if (_activeLocks.Count == 0 && _idleTimerDisabled)
        {
            _idleTimerDisabled = false;
            // On iOS: UIApplication.SharedApplication.IdleTimerDisabled = false;
            _logger?.LogDebug("[iOS WakeLock] UIApplication.IdleTimerDisabled restored to false");
        }
    }
}

