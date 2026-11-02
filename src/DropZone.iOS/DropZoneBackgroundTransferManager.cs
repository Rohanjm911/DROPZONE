using DropZone.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace DropZone.iOS;

public sealed class DropZoneBackgroundTransferManager
{
    private readonly IOSWakeLockService _wakeLockService;
    private readonly IOSNotificationService _notificationService;
    private readonly ILogger<DropZoneBackgroundTransferManager>? _logger;
    private bool _isBackgroundProcessingActive;
    private int _activeTransfers;

    public const string TaskIdentifier = "com.dropzone.transfer.background";

    public bool IsBackgroundProcessingActive => _isBackgroundProcessingActive;
    public int ActiveTransfersCount => _activeTransfers;

    public DropZoneBackgroundTransferManager(
        IOSWakeLockService wakeLockService,
        IOSNotificationService notificationService,
        ILogger<DropZoneBackgroundTransferManager>? logger = null)
    {
        _wakeLockService = wakeLockService;
        _notificationService = notificationService;
        _logger = logger;
    }

    public void OnTransferStarted(string jobId, string fileName)
    {
        _activeTransfers++;
        if (!_isBackgroundProcessingActive)
        {
            _isBackgroundProcessingActive = true;
            _logger?.LogInformation("[iOS BGTask] Scheduled BGProcessingTask '{TaskId}' (Active: {Count})", TaskIdentifier, _activeTransfers);
        }

        _ = _wakeLockService.AcquireWakeLockAsync(jobId, $"Transferring: {fileName}");
    }

    public void OnTransferEnded(string jobId)
    {
        _activeTransfers = Math.Max(0, _activeTransfers - 1);
        _ = _wakeLockService.ReleaseWakeLockAsync(jobId);

        if (_activeTransfers == 0 && _isBackgroundProcessingActive)
        {
            _isBackgroundProcessingActive = false;
            _logger?.LogInformation("[iOS BGTask] All background transfers finished, task completed");
        }
    }
}

