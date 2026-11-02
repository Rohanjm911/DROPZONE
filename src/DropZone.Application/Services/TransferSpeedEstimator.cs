namespace DropZone.Application.Services;

public sealed class TransferSpeedEstimator
{
    private readonly int _windowSizeSeconds;
    private readonly Queue<(DateTime Timestamp, long Bytes)> _samples = new();
    private long _lastReportedBytes;
    private double _smoothedSpeedBps;
    private readonly object _lock = new();

    public TransferSpeedEstimator(int windowSizeSeconds = 5)
    {
        _windowSizeSeconds = Math.Max(2, windowSizeSeconds);
    }

    public void RecordProgress(long currentTransferredBytes)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            _samples.Enqueue((now, currentTransferredBytes));
            _lastReportedBytes = currentTransferredBytes;

            // Evict samples older than window size
            var cutoff = now.AddSeconds(-_windowSizeSeconds);
            while (_samples.Count > 1 && _samples.Peek().Timestamp < cutoff)
            {
                _samples.Dequeue();
            }

            if (_samples.Count >= 2)
            {
                var oldest = _samples.Peek();
                var duration = (now - oldest.Timestamp).TotalSeconds;
                if (duration > 0.1)
                {
                    var deltaBytes = currentTransferredBytes - oldest.Bytes;
                    var instantSpeed = Math.Max(0.0, deltaBytes / duration);

                    // Exponential moving average: 70% smoothed + 30% instant
                    _smoothedSpeedBps = _smoothedSpeedBps <= 0.0 
                        ? instantSpeed 
                        : (_smoothedSpeedBps * 0.7) + (instantSpeed * 0.3);
                }
            }
        }
    }

    public double CurrentSpeedBytesPerSecond
    {
        get
        {
            lock (_lock)
            {
                return _smoothedSpeedBps;
            }
        }
    }

    public int CalculateEtaSeconds(long totalBytes)
    {
        lock (_lock)
        {
            if (_smoothedSpeedBps <= 1024.0 || totalBytes <= _lastReportedBytes)
                return 0;

            var remainingBytes = totalBytes - _lastReportedBytes;
            var seconds = remainingBytes / _smoothedSpeedBps;
            return (int)Math.Min(Math.Ceiling(seconds), 86400 * 7); // Cap at 7 days, round up to 1 second
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _samples.Clear();
            _lastReportedBytes = 0;
            _smoothedSpeedBps = 0.0;
        }
    }
}
