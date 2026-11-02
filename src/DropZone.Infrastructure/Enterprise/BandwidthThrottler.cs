using System.Diagnostics;
using DropZone.Domain.Interfaces;

namespace DropZone.Infrastructure.Enterprise;

public sealed class BandwidthThrottler : IBandwidthThrottler
{
    private long _bytesPerSecond;
    private double _availableTokens;
    private long _lastRefillTicks;
    private readonly object _lock = new();

    public long CurrentLimitBytesPerSecond => _bytesPerSecond;

    public BandwidthThrottler(long bytesPerSecond = 0)
    {
        SetLimit(bytesPerSecond);
    }

    public void SetLimit(long bytesPerSecond)
    {
        lock (_lock)
        {
            _bytesPerSecond = Math.Max(0, bytesPerSecond);
            _availableTokens = _bytesPerSecond;
            _lastRefillTicks = Stopwatch.GetTimestamp();
        }
    }

    public async Task ThrottleAsync(int bytesToSend, CancellationToken ct = default)
    {
        if (_bytesPerSecond <= 0 || bytesToSend <= 0)
            return;

        TimeSpan sleepDuration = TimeSpan.Zero;

        lock (_lock)
        {
            var nowTicks = Stopwatch.GetTimestamp();
            var elapsedSeconds = (double)(nowTicks - _lastRefillTicks) / Stopwatch.Frequency;
            _lastRefillTicks = nowTicks;

            // Refill tokens
            _availableTokens = Math.Min(_bytesPerSecond, _availableTokens + (elapsedSeconds * _bytesPerSecond));

            _availableTokens -= bytesToSend;

            if (_availableTokens < 0)
            {
                var deficit = -_availableTokens;
                var secondsNeeded = deficit / _bytesPerSecond;
                sleepDuration = TimeSpan.FromSeconds(Math.Min(secondsNeeded, 5.0)); // Cap single throttle delay at 5s
            }
        }

        if (sleepDuration > TimeSpan.Zero)
        {
            await Task.Delay(sleepDuration, ct);
        }
    }
}
