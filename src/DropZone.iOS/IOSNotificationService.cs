using System.Collections.Concurrent;
using DropZone.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace DropZone.iOS;

public sealed class IOSNotificationService : IPlatformNotificationService
{
    private readonly ILogger<IOSNotificationService>? _logger;
    private readonly ConcurrentDictionary<string, IOSNotificationEntry> _notifications = new();

    public sealed record IOSNotificationEntry(
        string Id,
        string Title,
        string Body,
        int BadgeCount,
        bool IsOngoing);

    public event EventHandler<IOSNotificationEntry>? NotificationChanged;

    public IReadOnlyCollection<IOSNotificationEntry> ActiveNotifications => _notifications.Values.ToArray();

    public IOSNotificationService(ILogger<IOSNotificationService>? logger = null)
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

        var etaText = etaSeconds > 0 ? $"{etaSeconds}s left" : "Calculating...";
        var title = isOngoing ? $"Transferring: {fileName} ({progressPercentage}%)" : $"Paused: {fileName}";
        var body = $"{speedText} • {etaText}";

        var entry = new IOSNotificationEntry(
            jobId,
            title,
            body,
            _notifications.Count + 1,
            isOngoing);

        _notifications[jobId] = entry;
        _logger?.LogDebug("[iOS Notification] {Title} - {Body}", title, body);
        NotificationChanged?.Invoke(this, entry);

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
        _notifications.TryRemove(jobId, out _);

        var sizeText = totalBytes >= 1_000_000_000
            ? $"{totalBytes / 1_000_000_000.0:F2} GB"
            : totalBytes >= 1_000_000
                ? $"{totalBytes / 1_000_000.0:F1} MB"
                : $"{totalBytes / 1_000.0:F1} KB";

        var title = isSuccess ? "Transfer Completed" : "Transfer Failed";
        var body = isSuccess
            ? $"Received '{fileName}' ({sizeText})"
            : $"Could not transfer '{fileName}': {errorMessage ?? "Unknown error"}";

        var entry = new IOSNotificationEntry(
            jobId,
            title,
            body,
            _notifications.Count,
            false);

        _logger?.LogInformation("[iOS Notification] {Title} - {Body}", title, body);
        NotificationChanged?.Invoke(this, entry);

        return Task.CompletedTask;
    }

    public Task ShowSecurityAlertNotificationAsync(
        string title,
        string message,
        string? details = null,
        CancellationToken ct = default)
    {
        var entry = new IOSNotificationEntry(
            "sec-" + Guid.NewGuid().ToString("N")[..8],
            title,
            $"{message} {details}".Trim(),
            _notifications.Count + 1,
            false);

        _notifications[entry.Id] = entry;
        _logger?.LogWarning("[iOS Security Alert] {Title}: {Body}", title, entry.Body);
        NotificationChanged?.Invoke(this, entry);

        return Task.CompletedTask;
    }

    public Task CancelNotificationAsync(string jobId, CancellationToken ct = default)
    {
        _notifications.TryRemove(jobId, out _);
        return Task.CompletedTask;
    }
}
