using DropZone.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace DropZone.Android;

public sealed class DropZoneTransferForegroundService
{
    private readonly AndroidWakeLockService _wakeLockService;
    private readonly AndroidNotificationService _notificationService;
    private readonly ILogger<DropZoneTransferForegroundService>? _logger;
    private bool _isServiceRunning;
    private int _activeTransfersCount;

    public bool IsServiceRunning => _isServiceRunning;
    public int ActiveTransfersCount => _activeTransfersCount;

    public DropZoneTransferForegroundService(
        AndroidWakeLockService wakeLockService,
        AndroidNotificationService notificationService,
        ILogger<DropZoneTransferForegroundService>? logger = null)
    {
        _wakeLockService = wakeLockService;
        _notificationService = notificationService;
        _logger = logger;
    }

    public void OnTransferStarted(string jobId, string fileName)
    {
        _activeTransfersCount++;
        if (!_isServiceRunning)
        {
            _isServiceRunning = true;
            _wakeLockService.AcquireMulticastLock();
            _logger?.LogInformation("[Android ForegroundService] Started Foreground Service (Active: {Count})", _activeTransfersCount);
        }

        _ = _wakeLockService.AcquireWakeLockAsync(jobId, $"Transferring: {fileName}");
    }

    public void OnTransferEnded(string jobId)
    {
        _activeTransfersCount = Math.Max(0, _activeTransfersCount - 1);
        _ = _wakeLockService.ReleaseWakeLockAsync(jobId);

        if (_activeTransfersCount == 0 && _isServiceRunning)
        {
            _isServiceRunning = false;
            _wakeLockService.ReleaseMulticastLock();
            _logger?.LogInformation("[Android ForegroundService] Stopped Foreground Service - all transfers completed");
        }
    }
}
