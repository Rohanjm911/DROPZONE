using System.Collections.Concurrent;
using DropZone.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace DropZone.Infrastructure.Platform;

public sealed class DesktopNotificationService : IPlatformNotificationService
{
    private readonly ILogger<DesktopNotificationService>? _logger;
    private readonly ConcurrentDictionary<string, string> _activeNotifications = new();

    public event EventHandler<(string JobId, string Title, string Message, int Progress)>? NotificationPosted;

    public DesktopNotificationService(ILogger<DesktopNotificationService>? logger = null)
    {
        _logger = logger;
    }

    public Task ShowTransferProgressNotificationAsync(
        string jobId,
        string fileName,
        int progressPercentage,
        double speedBps,
        int etaSeconds,
        bool isOngoing = true,
        CancellationToken ct = default)
    {
        var speedText = speedBps >= 1_000_000
            ? $"{speedBps / 1_000_000.0:F1} MB/s"
            : $"{speedBps / 1_000.0:F1} KB/s";

        var etaText = etaSeconds > 0 ? $"{etaSeconds}s remaining" : "calculating...";
        var title = isOngoing ? $"Transferring: {fileName} ({progressPercentage}%)" : $"Transfer Paused: {fileName}";
        var message = $"{speedText} • {etaText}";

        _activeNotifications[jobId] = $"{title} - {message}";
        _logger?.LogDebug("[Notification] {Title} - {Message}", title, message);

        NotificationPosted?.Invoke(this, (jobId, title, message, progressPercentage));
        return Task.CompletedTask;
    }

    public Task ShowTransferCompletedNotificationAsync(
        string jobId,
        string fileName,
        long totalBytes,
        bool isSuccess,
        string? errorMessage = null,
        CancellationToken ct = default)
    {
        _activeNotifications.TryRemove(jobId, out _);

        var sizeText = totalBytes >= 1_000_000_000
            ? $"{totalBytes / 1_000_000_000.0:F2} GB"
            : totalBytes >= 1_000_000
                ? $"{totalBytes / 1_000_000.0:F1} MB"
                : $"{totalBytes / 1_000.0:F1} KB";

        var title = isSuccess ? "Transfer Complete" : "Transfer Failed";
        var message = isSuccess
            ? $"Received '{fileName}' ({sizeText})"
            : $"Failed to transfer '{fileName}': {errorMessage ?? "Unknown error"}";

        _logger?.LogInformation("[Notification] {Title}: {Message}", title, message);
        NotificationPosted?.Invoke(this, (jobId, title, message, isSuccess ? 100 : -1));

        return Task.CompletedTask;
    }

    public Task ShowSecurityAlertNotificationAsync(
        string title,
        string message,
        string? details = null,
        CancellationToken ct = default)
    {
        _logger?.LogWarning("[Security Alert] {Title}: {Message} ({Details})", title, message, details);
        NotificationPosted?.Invoke(this, ("security-alert", title, $"{message} {details}".Trim(), -1));
        return Task.CompletedTask;
    }

    public Task CancelNotificationAsync(string jobId, CancellationToken ct = default)
    {
        _activeNotifications.TryRemove(jobId, out _);
        return Task.CompletedTask;
    }
}

