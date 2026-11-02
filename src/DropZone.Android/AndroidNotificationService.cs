using System.Collections.Concurrent;
using DropZone.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace DropZone.Android;

public sealed class AndroidNotificationService : IPlatformNotificationService
{
    private readonly ILogger<AndroidNotificationService>? _logger;
    private readonly ConcurrentDictionary<string, AndroidNotificationEntry> _notifications = new();

    public const string ChannelTransfers = "dropzone_channel_transfers";
    public const string ChannelAlerts = "dropzone_channel_alerts";

    public sealed record AndroidNotificationEntry(
        string Id,
        string Title,
        string Text,
        int Progress,
        bool IsOngoing,
        string ChannelId);

    public event EventHandler<AndroidNotificationEntry>? NotificationChanged;

    public IReadOnlyCollection<AndroidNotificationEntry> ActiveNotifications => _notifications.Values.ToArray();

    public AndroidNotificationService(ILogger<AndroidNotificationService>? logger = null)
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
        var text = $"{speedText} • {etaText}";

        var entry = new AndroidNotificationEntry(
            jobId,
            title,
            text,
            progressPercentage,
            isOngoing,
            ChannelTransfers);

        _notifications[jobId] = entry;
        _logger?.LogDebug("[Android Notification] {Title} - {Text}", title, text);
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

        var title = isSuccess ? "Transfer Complete" : "Transfer Failed";
        var text = isSuccess
            ? $"Successfully received '{fileName}' ({sizeText})"
            : $"Transfer of '{fileName}' failed: {errorMessage ?? "Unknown error"}";

        var entry = new AndroidNotificationEntry(
            jobId,
            title,
            text,
            isSuccess ? 100 : -1,
            false,
            ChannelTransfers);

        _logger?.LogInformation("[Android Notification] {Title} - {Text}", title, text);
        NotificationChanged?.Invoke(this, entry);

        return Task.CompletedTask;
    }

    public Task ShowSecurityAlertNotificationAsync(
        string title,
        string message,
        string? details = null,
        CancellationToken ct = default)
    {
        var entry = new AndroidNotificationEntry(
            "security-" + Guid.NewGuid().ToString("N")[..8],
            title,
            $"{message} {details}".Trim(),
            -1,
            false,
            ChannelAlerts);

        _notifications[entry.Id] = entry;
        _logger?.LogWarning("[Android Security Alert] {Title}: {Message}", title, message);
        NotificationChanged?.Invoke(this, entry);

        return Task.CompletedTask;
    }

    public Task CancelNotificationAsync(string jobId, CancellationToken ct = default)
    {
        _notifications.TryRemove(jobId, out _);
        return Task.CompletedTask;
    }
}
