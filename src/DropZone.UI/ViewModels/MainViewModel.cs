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

    [ObservableProperty]
    private string _currentTab = "Transfers"; // Transfers, Devices, History, Settings

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
        string defaultDownloadPath)
    {
        _orchestrator = orchestrator;
        _deviceRepo = deviceRepo;
        _transferRepo = transferRepo;
        _storage = storage;
        _downloadPath = defaultDownloadPath;

        _orchestrator.JobUpdated += OnJobUpdated;
        _orchestrator.InboundTransferRequested += OnInboundRequest;
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

        await _orchestrator.AcceptInboundTransferAsync(req.JobId);
    }

    [RelayCommand]
    public async Task RejectIncomingAsync()
    {
        if (IncomingRequest == null) return;
        var req = IncomingRequest;
        IncomingRequest = null;
        HasIncomingRequest = false;

        await _orchestrator.RejectInboundTransferAsync(req.JobId, "Rejected by recipient");
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
        });
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

