using System.Collections.Concurrent;
using DropZone.Domain.Interfaces;

namespace DropZone.Infrastructure.Platform;

public sealed class DesktopLifecycleService : IPlatformLifecycleService
{
    private AppLifecycleState _currentState = AppLifecycleState.Foreground;
    private readonly ConcurrentDictionary<string, byte> _activeBackgroundTransfers = new();

    public AppLifecycleState CurrentState
    {
        get => _currentState;
        private set
        {
            if (_currentState != value)
            {
                _currentState = value;
                LifecycleStateChanged?.Invoke(this, value);
            }
        }
    }

    public event EventHandler<AppLifecycleState>? LifecycleStateChanged;

    public void SetState(AppLifecycleState newState)
    {
        CurrentState = newState;
    }

    public Task NotifyBackgroundTransferStartingAsync(string jobId, CancellationToken ct = default)
    {
        _activeBackgroundTransfers[jobId] = 1;
        return Task.CompletedTask;
    }

    public Task NotifyBackgroundTransferFinishedAsync(string jobId, CancellationToken ct = default)
    {
        _activeBackgroundTransfers.TryRemove(jobId, out _);
        return Task.CompletedTask;
    }

    public int ActiveBackgroundTransfersCount => _activeBackgroundTransfers.Count;
}
