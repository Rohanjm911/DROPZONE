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

    public bool IsTransfersTab => CurrentTab == "Transfers";
    public bool IsDevicesTab => CurrentTab == "Devices";
    public bool IsHistoryTab => CurrentTab == "History";
    public bool IsAuditTab => CurrentTab == "Audit";
    public bool IsDiagnosticsTab => CurrentTab == "Diagnostics";
    public bool IsSettingsTab => CurrentTab == "Settings";

    partial void OnCurrentTabChanged(string value)
    {
        OnPropertyChanged(nameof(IsTransfersTab));
        OnPropertyChanged(nameof(IsDevicesTab));
        OnPropertyChanged(nameof(IsHistoryTab));
        OnPropertyChanged(nameof(IsAuditTab));
        OnPropertyChanged(nameof(IsDiagnosticsTab));
        OnPropertyChanged(nameof(IsSettingsTab));
    }

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

    [ObservableProperty]
    private string _deviceSearchQuery = string.Empty;

    [ObservableProperty]
    private string _deviceFilter = "All"; // All, Online, Trusted

    [ObservableProperty]
    private string _historySearchQuery = string.Empty;

    [ObservableProperty]
    private string _historyFilter = "All"; // All, Completed, Cancelled, Quarantined

    [ObservableProperty]
    private string _auditFilter = "All"; // All, Transfers, Security

    [ObservableProperty]
    private string _bandwidthLimit = "Unlimited";

    [ObservableProperty]
    private bool _quarantineSuspicious = true;

    [ObservableProperty]
    private bool _allowRemoteStun = true;

    [ObservableProperty]
    private bool _enableMdns = true;

    [ObservableProperty]
    private string _manualDeviceIp = string.Empty;

    [ObservableProperty]
    private string _manualDevicePort = "52490";

    [ObservableProperty]
    private string _manualDeviceStatus = string.Empty;

    public IEnumerable<DeviceItemViewModel> FilteredDevices
    {
        get
        {
            var list = NearbyDevices.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(DeviceSearchQuery))
            {
                var q = DeviceSearchQuery.Trim().ToLowerInvariant();
                list = list.Where(d => d.Name.ToLowerInvariant().Contains(q) || d.Endpoint.Contains(q));
            }
            if (DeviceFilter == "Online")
                list = list.Where(d => d.IsOnline);
            else if (DeviceFilter == "Trusted")
                list = list.Where(d => d.TrustBadge == "TRUSTED");
            return list;
        }
    }

    public IEnumerable<TransferItemViewModel> FilteredHistory
    {
        get
        {
            var list = HistoryTransfers.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(HistorySearchQuery))
            {
                var q = HistorySearchQuery.Trim().ToLowerInvariant();
                list = list.Where(t => t.PrimaryFileName.ToLowerInvariant().Contains(q) || t.PeerName.ToLowerInvariant().Contains(q));
            }
            if (HistoryFilter == "Completed")
                list = list.Where(t => t.Job.State == TransferState.Completed);
            else if (HistoryFilter == "Cancelled")
                list = list.Where(t => t.Job.State == TransferState.Cancelled || t.Job.State == TransferState.Failed);
            else if (HistoryFilter == "Quarantined")
                list = list.Where(t => t.Job.State == TransferState.Quarantined);
            return list;
        }
    }

    public IEnumerable<ComplianceAuditEntry> FilteredAuditLog
    {
        get
        {
            var list = AuditLog.AsEnumerable();
            if (AuditFilter == "Transfers")
                list = list.Where(a => a.EventType == AuditEventType.TransferStarted || a.EventType == AuditEventType.TransferCompleted || a.EventType == AuditEventType.TransferCancelled || a.EventType == AuditEventType.TransferFailed);
            else if (AuditFilter == "Security")
                list = list.Where(a => a.EventType == AuditEventType.SecurityAlertTriggered || a.EventType == AuditEventType.FileQuarantined || a.EventType == AuditEventType.DeviceEnrolled || a.EventType == AuditEventType.DeviceBlocked || a.EventType == AuditEventType.PolicyUpdated);
            return list;
        }
    }

    partial void OnDeviceSearchQueryChanged(string value) => OnPropertyChanged(nameof(FilteredDevices));
    partial void OnDeviceFilterChanged(string value) => OnPropertyChanged(nameof(FilteredDevices));
    partial void OnHistorySearchQueryChanged(string value) => OnPropertyChanged(nameof(FilteredHistory));
    partial void OnHistoryFilterChanged(string value) => OnPropertyChanged(nameof(FilteredHistory));
    partial void OnAuditFilterChanged(string value) => OnPropertyChanged(nameof(FilteredAuditLog));

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

        NearbyDevices.CollectionChanged += (_, _) => OnPropertyChanged(nameof(FilteredDevices));
        HistoryTransfers.CollectionChanged += (_, _) => OnPropertyChanged(nameof(FilteredHistory));
        AuditLog.CollectionChanged += (_, _) => OnPropertyChanged(nameof(FilteredAuditLog));
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

    [RelayCommand]
    public void SetDeviceFilter(string filter) => DeviceFilter = filter;

    [RelayCommand]
    public void SetHistoryFilter(string filter) => HistoryFilter = filter;

    [RelayCommand]
    public void SetAuditFilter(string filter) => AuditFilter = filter;

    [RelayCommand]
    public async Task PickAndSendFolderAsync()
    {
        if (_storagePicker == null) return;
        var folder = await _storagePicker.PickFolderAsync("Select folder to send with DropZone");
        if (!string.IsNullOrEmpty(folder))
        {
            await HandleDroppedFilesAsync(new[] { folder });
        }
    }

    [RelayCommand]
    public async Task ChangeDownloadPathAsync()
    {
        if (_storagePicker == null) return;
        var folder = await _storagePicker.PickFolderAsync("Select New Download Directory");
        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
        {
            DownloadPath = folder;
        }
    }

    [RelayCommand]
    public void OpenDownloadsFolder()
    {
        if (Directory.Exists(DownloadPath))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = DownloadPath,
                    UseShellExecute = true
                });
            }
            catch { }
        }
    }

    [RelayCommand]
    public void ClearCompletedTransfers()
    {
        var toRemove = ActiveTransfers
            .Where(t => t.Job.State == TransferState.Completed || t.Job.State == TransferState.Cancelled || t.Job.State == TransferState.Failed)
            .ToList();
        foreach (var item in toRemove)
        {
            ActiveTransfers.Remove(item);
        }
    }

    [RelayCommand]
    public void ClearHistoryTransfers()
    {
        HistoryTransfers.Clear();
        OnPropertyChanged(nameof(FilteredHistory));
    }

    [RelayCommand]
    public void OpenTransferFile(TransferItemViewModel? item)
    {
        if (item == null) return;
        var fullPath = Path.Combine(DownloadPath, item.PrimaryFileName);
        if (File.Exists(fullPath))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = fullPath,
                    UseShellExecute = true
                });
            }
            catch { }
        }
        else
        {
            OpenDownloadsFolder();
        }
    }

    [RelayCommand]
    public void OpenTransferFolder(TransferItemViewModel? item)
    {
        OpenDownloadsFolder();
    }

    [RelayCommand]
    public async Task AddManualDeviceAsync()
    {
        if (string.IsNullOrWhiteSpace(ManualDeviceIp))
        {
            ManualDeviceStatus = "Enter a valid IP address";
            return;
        }

        if (!ushort.TryParse(ManualDevicePort, out var port))
        {
            port = 52490;
        }

        var dev = new Device
        {
            Id = $"manual-{ManualDeviceIp.Trim().Replace('.', '-')}-{port}",
            Name = $"Peer ({ManualDeviceIp.Trim()})",
            IpAddress = ManualDeviceIp.Trim(),
            Port = port,
            TrustLevel = DeviceTrustLevel.Prompt,
            LastSeen = DateTime.UtcNow
        };

        await _deviceRepo.UpsertDeviceAsync(dev);
        AddOrUpdateDevice(dev);
        ManualDeviceStatus = $"Device added: {dev.IpAddress}:{dev.Port}";
        ManualDeviceIp = string.Empty;
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

