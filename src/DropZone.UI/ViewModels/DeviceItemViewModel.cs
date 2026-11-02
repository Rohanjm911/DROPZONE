using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DropZone.Domain.Enums;
using DropZone.Domain.Interfaces;
using DropZone.Domain.Models;

namespace DropZone.UI.ViewModels;

public sealed partial class DeviceItemViewModel : ViewModelBase
{
    private readonly IDeviceRepository _deviceRepo;

    [ObservableProperty]
    private Device _device;

    public event Action<Device>? SendRequested;

    public DeviceItemViewModel(Device device, IDeviceRepository deviceRepo)
    {
        _device = device;
        _deviceRepo = deviceRepo;
    }

    public string Id => Device.Id;
    public string Name => Device.Name;
    public string Endpoint => $"{Device.IpAddress}:{Device.Port}";
    public bool IsOnline => Device.IsOnline;

    public string StatusText => Device.IsOnline ? "ONLINE" : "OFFLINE";
    public string StatusColor => Device.IsOnline ? "#10B981" : "#646472";

    public string TrustBadge => Device.TrustLevel switch
    {
        DeviceTrustLevel.Trusted => "TRUSTED",
        DeviceTrustLevel.Blocked => "BLOCKED",
        _ => "UNVERIFIED"
    };

    public string TrustColor => Device.TrustLevel switch
    {
        DeviceTrustLevel.Trusted => "#10B981",
        DeviceTrustLevel.Blocked => "#EF4444",
        _ => "#F59E0B"
    };

    [RelayCommand]
    public void SendFiles()
    {
        SendRequested?.Invoke(Device);
    }

    [RelayCommand]
    public async Task ToggleTrustAsync()
    {
        var newTrust = Device.TrustLevel == DeviceTrustLevel.Trusted 
            ? DeviceTrustLevel.Prompt 
            : DeviceTrustLevel.Trusted;

        Device.TrustLevel = newTrust;
        await _deviceRepo.UpdateTrustLevelAsync(Device.Id, newTrust);
        OnPropertyChanged(nameof(TrustBadge));
        OnPropertyChanged(nameof(TrustColor));
    }
}

