using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DropZone.Application.Services;
using DropZone.Domain.Enums;
using DropZone.Domain.Models;

namespace DropZone.UI.ViewModels;

public sealed partial class TransferItemViewModel : ViewModelBase
{
    private readonly TransferOrchestrator _orchestrator;

    [ObservableProperty]
    private TransferJob _job;

    public TransferItemViewModel(TransferJob job, TransferOrchestrator orchestrator)
    {
        _job = job;
        _orchestrator = orchestrator;
    }

    public Guid Id => Job.Id;
    public string PeerName => Job.PeerDeviceName;
    public string DirectionLabel => Job.Direction == TransferDirection.Inbound ? "Incoming from" : "Sending to";
    public string PrimaryFileName => Job.Files.FirstOrDefault()?.RelativePath ?? "Unknown file";
    public int AdditionalFilesCount => Math.Max(0, Job.Files.Count - 1);
    public string AdditionalFilesLabel => AdditionalFilesCount > 0 ? $"+{AdditionalFilesCount} more files" : string.Empty;

    public double Progress => Job.ProgressPercentage;
    public string FormattedProgress => $"{Job.ProgressPercentage:0.0}%";
    public string FormattedSize => $"{FormatBytes(Job.TransferredBytes)} of {FormatBytes(Job.TotalBytes)}";
    public string FormattedSpeed => Job.State == TransferState.Transferring && Job.SpeedBytesPerSec > 0
        ? $"{FormatBytes((long)Job.SpeedBytesPerSec)}/s"
        : string.Empty;

    public string FormattedEta
    {
        get
        {
            if (Job.State != TransferState.Transferring || Job.EtaSeconds <= 0)
                return string.Empty;

            var span = TimeSpan.FromSeconds(Job.EtaSeconds);
            if (span.TotalHours >= 1)
                return $"{span.Hours}h {span.Minutes}m remaining";
            if (span.TotalMinutes >= 1)
                return $"{span.Minutes}m {span.Seconds}s remaining";
            return $"{span.Seconds}s remaining";
        }
    }

    public string StateBadge => Job.State switch
    {
        TransferState.Pending => "QUEUED",
        TransferState.Connecting => "CONNECTING",
        TransferState.Transferring => "TRANSFERRING",
        TransferState.Paused => "PAUSED",
        TransferState.Completed => "COMPLETED",
        TransferState.Failed => "FAILED",
        TransferState.Cancelled => "CANCELLED",
        TransferState.Quarantined => "QUARANTINED",
        _ => "UNKNOWN"
    };

    public string StateColor => Job.State switch
    {
        TransferState.Transferring => "#0284C7", // Primary Obsidian Cyan
        TransferState.Completed => "#10B981",    // Success Emerald
        TransferState.Failed => "#EF4444",       // Danger Crimson
        TransferState.Cancelled => "#64748B",    // Muted Slate
        TransferState.Paused => "#F59E0B",       // Warning Amber
        TransferState.Quarantined => "#EF4444",  // Danger Crimson
        _ => "#94A3B8"                           // Neutral Slate
    };

    public bool CanPause => Job.State == TransferState.Transferring;
    public bool CanResume => Job.State == TransferState.Paused || Job.State == TransferState.Failed;
    public bool CanCancel => Job.State != TransferState.Completed && Job.State != TransferState.Cancelled;

    [RelayCommand]
    public async Task PauseAsync()
    {
        await _orchestrator.PauseTransferAsync(Job.Id);
        Refresh();
    }

    [RelayCommand]
    public async Task ResumeAsync()
    {
        await _orchestrator.ResumeTransferAsync(Job.Id);
        Refresh();
    }

    [RelayCommand]
    public async Task CancelAsync()
    {
        await _orchestrator.CancelTransferAsync(Job.Id);
        Refresh();
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(Job));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(FormattedProgress));
        OnPropertyChanged(nameof(FormattedSize));
        OnPropertyChanged(nameof(FormattedSpeed));
        OnPropertyChanged(nameof(FormattedEta));
        OnPropertyChanged(nameof(StateBadge));
        OnPropertyChanged(nameof(StateColor));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanResume));
        OnPropertyChanged(nameof(CanCancel));
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.0} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):0.0} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):0.00} GB";
    }
}

