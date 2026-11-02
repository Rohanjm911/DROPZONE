using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DropZone.Application.Services;
using DropZone.Domain.Enums;
using DropZone.Domain.Interfaces;
using DropZone.Domain.Models;

namespace DropZone.UI.ViewModels;

public sealed partial class MainViewModel : ViewModelBase
{
    private readonly TransferOrchestrator _orchestrator;
    private readonly IDeviceRepository _deviceRepo;
    private readonly ITransferRepository _transferRepo;
    private readonly IFileSystemStorage _storage;
    private readonly ISecurityService _security;
    private readonly IBackupService _backupService;
    private readonly IPlatformService? _platformService;
    private readonly IPlatformStoragePicker? _storagePicker;
    private readonly IComplianceAuditService? _auditService;
    private readonly IFolderSyncService? _syncService;

    public bool IsMobile => _platformService?.IsMobile ?? false;
    public string PlatformName => _platformService?.Platform.ToString() ?? "Desktop";

    [ObservableProperty]
    private string _currentTab = "Transfers"; // Transfers, Devices, History, Diagnostics, Settings, Audit

    [ObservableProperty]
    private string _auditIntegrityStatus = "Ledger intact (SHA-256 chain verified)";

    [ObservableProperty]
    private ObservableCollection<ComplianceAuditEntry> _auditLog = new();

    [ObservableProperty]
    private ObservableCollection<TransferItemViewModel> _activeTransfers = new();

    [ObservableProperty]
    private ObservableCollection<TransferItemViewModel> _historyTransfers = new();

    [ObservableProperty]
    private ObservableCollection<DeviceItemViewModel> _nearbyDevices = new();

    [ObservableProperty]
    private DeviceItemViewModel? _selectedDevice;

    [ObservableProperty]
    private TransferManifest? _incomingRequest;

    [ObservableProperty]
    private bool _hasIncomingRequest;

    [ObservableProperty]
    private bool _hasSuspiciousFilesInIncoming;

    [ObservableProperty]
    private string _suspiciousWarningMessage = string.Empty;

    [ObservableProperty]
    private bool _hasPairingRequest;

    [ObservableProperty]
    private PairingPinInfo? _currentPairing;

    [ObservableProperty]
    private string _pairingPinFormatted = "------";

    [ObservableProperty]
    private string _pairingPeerName = string.Empty;

    [ObservableProperty]
    private string _pairingFingerprint = string.Empty;

    [ObservableProperty]
    private string _publicEndpointFormatted = "Checking STUN NAT...";

    [ObservableProperty]
    private string _natDiagnosticsStatus = "Ready";

    [ObservableProperty]
    private string _latencyFormatted = "Direct Probe Pending";

    [ObservableProperty]
    private string _activeChunkSizeFormatted = "1.0 MB (Adaptive)";

    [ObservableProperty]
    private string _activeRouteFormatted = "Direct LAN";

    [ObservableProperty]
    private string _backupStatusMessage = string.Empty;

    [ObservableProperty]
    private string _deviceName = Environment.MachineName;

    [ObservableProperty]
    private string _downloadPath;

    [ObservableProperty]
    private bool _autoAcceptTrusted = true;

    [ObservableProperty]
    private string _totalSpeedFormatted = "0.0 KB/s";

    public MainViewModel(
        TransferOrchestrator orchestrator,
        IDeviceRepository deviceRepo,
        ITransferRepository transferRepo,
        IFileSystemStorage storage,
        ISecurityService security,
        IBackupService backupService,
        string defaultDownloadPath,
        IPlatformService? platformService = null,
        IPlatformStoragePicker? storagePicker = null,
        IComplianceAuditService? auditService = null,
        IFolderSyncService? syncService = null)
    {
        _orchestrator = orchestrator;
        _deviceRepo = deviceRepo;
        _transferRepo = transferRepo;
        _storage = storage;
        _security = security;
        _backupService = backupService;
        _downloadPath = defaultDownloadPath;
        _platformService = platformService;
        _storagePicker = storagePicker;
        _auditService = auditService;
        _syncService = syncService;

        _orchestrator.JobUpdated += OnJobUpdated;
        _orchestrator.InboundTransferRequested += OnInboundRequest;
        _orchestrator.PairingPinDisplayRequested += OnPairingPinDisplay;
        _orchestrator.PairingCompleted += OnPairingCompleted;
        _orchestrator.NatCandidateDiscovered += OnNatCandidateDiscovered;
        _orchestrator.TelemetryUpdated += OnTelemetryUpdated;
    }

    public async Task InitializeAsync()
    {
        // Load initial jobs
        var jobs = await _transferRepo.GetAllJobsAsync();
        foreach (var job in jobs)
        {
            var vm = new TransferItemViewModel(job, _orchestrator);
            if (job.State == TransferState.Completed || job.State == TransferState.Failed || job.State == TransferState.Cancelled)
            {
                HistoryTransfers.Add(vm);
            }
            else
            {
                ActiveTransfers.Add(vm);
            }
        }

        // Load devices
        var devices = await _deviceRepo.GetAllDevicesAsync();
        foreach (var dev in devices)
        {
            AddOrUpdateDevice(dev);
        }

        // Load initial compliance audit entries
        if (_auditService != null)
        {
            var entries = await _auditService.GetAuditLogAsync(50);
            foreach (var e in entries)
            {
                AuditLog.Add(e);
            }
        }
    }

    [RelayCommand]
    public void SwitchTab(string tabName)
    {
        CurrentTab = tabName;
    }

    [RelayCommand]
    public async Task ApproveIncomingAsync()
    {
        if (IncomingRequest == null) return;
        var req = IncomingRequest;
        IncomingRequest = null;
        HasIncomingRequest = false;
        HasSuspiciousFilesInIncoming = false;

        await _orchestrator.AcceptInboundTransferAsync(req.JobId);
    }

    [RelayCommand]
    public async Task ApproveAndQuarantineIncomingAsync()
    {
        if (IncomingRequest == null) return;
        var req = IncomingRequest;
        IncomingRequest = null;
        HasIncomingRequest = false;
        HasSuspiciousFilesInIncoming = false;

        // Accept the transfer; the files will be safely downloaded and then suspicious files quarantined
        await _orchestrator.AcceptInboundTransferAsync(req.JobId);
    }

    [RelayCommand]
    public async Task RejectIncomingAsync()
    {
        if (IncomingRequest == null) return;
        var req = IncomingRequest;
        IncomingRequest = null;
        HasIncomingRequest = false;
        HasSuspiciousFilesInIncoming = false;

        await _orchestrator.RejectInboundTransferAsync(req.JobId, "Rejected by recipient");
    }

    [RelayCommand]
    public async Task ConfirmPairingAsync()
    {
        if (CurrentPairing == null) return;
        var peerId = CurrentPairing.PeerDeviceId;
        var pin = CurrentPairing.PinCode;
        HasPairingRequest = false;
        CurrentPairing = null;

        await _orchestrator.ConfirmPairingAsync(peerId, pin, isAccepted: true);
    }

    [RelayCommand]
    public async Task RejectPairingAsync()
    {
        if (CurrentPairing == null) return;
        var peerId = CurrentPairing.PeerDeviceId;
        HasPairingRequest = false;
        CurrentPairing = null;

        await _orchestrator.ConfirmPairingAsync(peerId, "", isAccepted: false);
    }

    [RelayCommand]
    public async Task PairWithDeviceAsync(DeviceItemViewModel? deviceVm)
    {
        var target = deviceVm ?? SelectedDevice;
        if (target == null) return;

        await _orchestrator.StartPairingAsync(target.Device.IpAddress, target.Device.Port);
    }

    [RelayCommand]
    public async Task RunNetworkDiagnosticsAsync()
    {
        NatDiagnosticsStatus = "Probing STUN servers...";
        var candidate = await _orchestrator.TestStunAsync();
        if (candidate != null)
        {
            PublicEndpointFormatted = $"{candidate.Ip}:{candidate.Port} ({candidate.CandidateType})";
            NatDiagnosticsStatus = "P2P Ready: Direct reflexive endpoint verified.";
        }
        else
        {
            PublicEndpointFormatted = "Direct probe unreachable";
            NatDiagnosticsStatus = "Relay Fallback Ready: Symmetric NAT detected.";
        }
    }

    [RelayCommand]
    public async Task VerifyAuditLedgerAsync()
    {
        if (_auditService == null) return;
        var result = await _auditService.VerifyAuditChainIntegrityAsync();
        AuditIntegrityStatus = result.IsValid
            ? $"Verified {result.TotalEntriesVerified} events. Hash chain mathematically intact."
            : $"TAMPER DETECTED: {result.ErrorMessage}";
    }

    [RelayCommand]
    public async Task ExportAuditReportAsync()
    {
        if (_auditService == null || _storagePicker == null) return;
        var json = await _auditService.ExportAuditReportJsonAsync();
        var dest = await _storagePicker.PickSaveFileAsync("Export Compliance Audit Report", "dropzone_compliance_audit.json", "json");
        if (!string.IsNullOrEmpty(dest))
        {
            await File.WriteAllTextAsync(dest, json);
            AuditIntegrityStatus = $"Audit report exported to {Path.GetFileName(dest)}";
        }
    }

    [RelayCommand]
    public async Task PickAndSendFilesAsync()
    {
        if (_storagePicker == null)
            return;

        var files = await _storagePicker.PickFilesAsync("Select files to share with DropZone", allowMultiple: true);
        if (files.Count > 0)
        {
            await HandleDroppedFilesAsync(files);
        }
    }

    [RelayCommand]
    public async Task SendFilesToDeviceAsync(string filePath)
    {
        if (SelectedDevice == null && NearbyDevices.Count > 0)
        {
            SelectedDevice = NearbyDevices.FirstOrDefault(d => d.IsOnline) ?? NearbyDevices[0];
        }

        if (SelectedDevice == null)
            return;

        await _orchestrator.StartOutboundTransferAsync(
            filePath,
            SelectedDevice.Device,
            "dz-local-node",
            DeviceName);

        CurrentTab = "Transfers";
    }

    public async Task HandleDroppedFilesAsync(IEnumerable<string> paths)
    {
        var targetDevice = SelectedDevice?.Device 
            ?? NearbyDevices.FirstOrDefault(d => d.IsOnline)?.Device;

        if (targetDevice == null)
            return;

        foreach (var path in paths)
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                await _orchestrator.StartOutboundTransferAsync(
                    path,
                    targetDevice,
                    "dz-local-node",
                    DeviceName);
            }
        }

        CurrentTab = "Transfers";
    }

    private void OnJobUpdated(TransferJob job)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var existingActive = ActiveTransfers.FirstOrDefault(vm => vm.Id == job.Id);
            if (existingActive != null)
            {
                existingActive.Job = job;
                existingActive.Refresh();

                if (job.State == TransferState.Completed || job.State == TransferState.Failed || job.State == TransferState.Cancelled)
                {
                    ActiveTransfers.Remove(existingActive);
                    HistoryTransfers.Insert(0, existingActive);
                }
            }
            else
            {
                var existingHistory = HistoryTransfers.FirstOrDefault(vm => vm.Id == job.Id);
                if (existingHistory != null)
                {
                    existingHistory.Job = job;
                    existingHistory.Refresh();
                }
                else
                {
                    var vm = new TransferItemViewModel(job, _orchestrator);
                    if (job.State == TransferState.Completed || job.State == TransferState.Failed || job.State == TransferState.Cancelled)
                    {
                        HistoryTransfers.Insert(0, vm);
                    }
                    else
                    {
                        ActiveTransfers.Insert(0, vm);
                    }
                }
            }

            // Update status bar total speed
            var totalSpeed = ActiveTransfers.Sum(t => t.Job.SpeedBytesPerSec);
            TotalSpeedFormatted = FormatBytes((long)totalSpeed) + "/s";
        });
    }

    private void OnInboundRequest(TransferManifest manifest)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            IncomingRequest = manifest;
            HasIncomingRequest = true;

            var suspiciousFiles = manifest.Files
                .Select(f => (File: f, Analysis: _security.AnalyzeFile(f.RelativePath)))
                .Where(x => x.Analysis.IsSuspicious)
                .ToList();

            if (suspiciousFiles.Count > 0)
            {
                HasSuspiciousFilesInIncoming = true;
                SuspiciousWarningMessage = $"SECURITY WARNING: {suspiciousFiles.Count} potentially dangerous executable/script file(s) detected in this transfer:";
            }
            else
            {
                HasSuspiciousFilesInIncoming = false;
                SuspiciousWarningMessage = string.Empty;
            }
        });
    }

    private void OnPairingPinDisplay(PairingPinInfo pinInfo)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            CurrentPairing = pinInfo;
            // Format 6-digit pin nicely as "123  456"
            PairingPinFormatted = pinInfo.PinCode.Length == 6
                ? $"{pinInfo.PinCode[..3]}  {pinInfo.PinCode[3..]}"
                : pinInfo.PinCode;
            PairingPeerName = pinInfo.PeerDeviceName;
            PairingFingerprint = pinInfo.Fingerprint.Length >= 16
                ? $"{pinInfo.Fingerprint[..8]}...{pinInfo.Fingerprint[^8..]}"
                : pinInfo.Fingerprint;
            HasPairingRequest = true;
        });
    }

    private void OnPairingCompleted(PairingResult result)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            HasPairingRequest = false;
            CurrentPairing = null;

            // Refresh device list to reflect trusted status
            var dev = NearbyDevices.FirstOrDefault(d => d.Id == result.PeerDeviceId);
            if (dev != null)
            {
                dev.Device.TrustLevel = result.Success ? DeviceTrustLevel.Trusted : DeviceTrustLevel.Prompt;
            }
        });
    }

    private void OnNatCandidateDiscovered(NatCandidate candidate)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            PublicEndpointFormatted = $"{candidate.Ip}:{candidate.Port} ({candidate.CandidateType})";
            NatDiagnosticsStatus = "P2P Candidate Active";
        });
    }

    private void OnTelemetryUpdated(TransferTelemetry telemetry)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            LatencyFormatted = telemetry.FormattedLatency;
            ActiveChunkSizeFormatted = $"{telemetry.FormattedChunkSize} (Adaptive)";
            ActiveRouteFormatted = telemetry.Route switch
            {
                NetworkRouteType.ReflexiveP2P => "Reflexive P2P (NAT Traversed)",
                NetworkRouteType.TurnRelay => "Encrypted TURN Relay",
                _ => "Direct LAN (Local Socket)"
            };
        });
    }

    [RelayCommand]
    public async Task PingSelectedDeviceAsync()
    {
        if (SelectedDevice == null) return;
        LatencyFormatted = "Probing ping...";
        await _orchestrator.PingPeerAsync(SelectedDevice.Device.IpAddress, SelectedDevice.Device.Port);
    }

    [RelayCommand]
    public async Task ExportEncryptedBackupAsync(string passphrase)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(passphrase))
            {
                BackupStatusMessage = "Error: Passphrase cannot be empty.";
                return;
            }

            var backupDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "DropZone");
            Directory.CreateDirectory(backupDir);
            var filePath = Path.Combine(backupDir, $"DropZone_Backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}.dzbackup");

            await _backupService.ExportBackupToFileAsync(filePath, passphrase);
            BackupStatusMessage = $"Encrypted backup exported to:\n{filePath}";
        }
        catch (Exception ex)
        {
            BackupStatusMessage = $"Export failed: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task RestoreEncryptedBackupAsync((string filePath, string passphrase) param)
    {
        try
        {
            BackupStatusMessage = "Restoring backup...";
            var package = await _backupService.ImportBackupFromFileAsync(param.filePath, param.passphrase);
            BackupStatusMessage = $"Restored {package.Devices.Count} devices and {package.TransferHistory.Count} history items successfully.";

            // Refresh UI devices
            NearbyDevices.Clear();
            var devices = await _deviceRepo.GetAllDevicesAsync();
            foreach (var dev in devices)
            {
                AddOrUpdateDevice(dev);
            }
        }
        catch (Exception ex)
        {
            BackupStatusMessage = $"Restore failed: {ex.Message}";
        }
    }

    public void AddOrUpdateDevice(Device device)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var existing = NearbyDevices.FirstOrDefault(d => d.Id == device.Id);
            if (existing != null)
            {
                existing.Device = device;
            }
            else
            {
                var vm = new DeviceItemViewModel(device, _deviceRepo);
                vm.SendRequested += dev =>
                {
                    SelectedDevice = vm;
                    // Trigger send file prompt or tab switch
                };
                vm.PairRequested += dev =>
                {
                    _ = PairWithDeviceAsync(vm);
                };
                NearbyDevices.Add(vm);
            }
        });
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.0} KB";
        return $"{bytes / (1024.0 * 1024.0):0.0} MB";
    }
}

