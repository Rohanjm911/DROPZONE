using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using DropZone.Domain.Interfaces;

namespace DropZone.Infrastructure.Platform;

public sealed class DesktopWakeLockService : IPlatformWakeLockService
{
    private readonly ConcurrentDictionary<string, string> _activeLocks = new();
    private readonly object _stateLock = new();

    // Windows Execution State Flags
    private const uint ES_SYSTEM_REQUIRED = 0x00000001;
    private const uint ES_CONTINUOUS = 0x80000000;
    private const uint ES_AWAYMODE_REQUIRED = 0x00000040;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint esFlags);

    public IReadOnlyCollection<string> ActiveLocks => _activeLocks.Keys.ToArray();

    public bool IsLockActive(string lockId) => _activeLocks.ContainsKey(lockId);

    public Task<bool> AcquireWakeLockAsync(string lockId, string reason, CancellationToken ct = default)
    {
        lock (_stateLock)
        {
            _activeLocks[lockId] = reason;
            UpdateSystemExecutionState();
        }

        return Task.FromResult(true);
    }

    public Task<bool> ReleaseWakeLockAsync(string lockId, CancellationToken ct = default)
    {
        lock (_stateLock)
        {
            _activeLocks.TryRemove(lockId, out _);
            UpdateSystemExecutionState();
        }

        return Task.FromResult(true);
    }

    private void UpdateSystemExecutionState()
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            if (_activeLocks.Count > 0)
            {
                // Prevent machine from sleeping while active transfers are running
                SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_AWAYMODE_REQUIRED);
            }
            else
            {
                // Restore standard power management
                SetThreadExecutionState(ES_CONTINUOUS);
            }
        }
        catch
        {
            // Non-critical if execution state cannot be toggled
        }
    }
}
