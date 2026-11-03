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
    public event Action<Device>? PairRequested;

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
    public string StatusColor => Device.IsOnline ? "#10B981" : "#64748B";

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

    public string PlatformIcon
    {
        get
        {
            var name = Device.Name.ToUpperInvariant();
            if (name.Contains("IPHONE") || name.Contains("IPAD") || name.Contains("MAC") || name.Contains("APPLE"))
                return "🍏";
            if (name.Contains("ANDROID") || name.Contains("PIXEL") || name.Contains("GALAXY") || name.Contains("SAMSUNG") || name.Contains("XIAOMI"))
                return "🤖";
            if (name.Contains("LINUX") || name.Contains("UBUNTU") || name.Contains("DEBIAN") || name.Contains("ARCH"))
                return "🐧";
            if (name.Contains("WIN") || name.Contains("PC") || name.Contains("DESKTOP") || name.Contains("LAPTOP"))
                return "🪟";
            return "💻";
        }
    }

    public string FingerprintShort => string.IsNullOrEmpty(Device.PublicKeyFingerprint)
        ? "ECDH Curve25519"
        : (Device.PublicKeyFingerprint.Length > 16 ? Device.PublicKeyFingerprint[..16] + "..." : Device.PublicKeyFingerprint);

    public string LastSeenFormatted
    {
        get
        {
            var sec = (DateTime.UtcNow - Device.LastSeen).TotalSeconds;
            if (sec < 10) return "Active now";
            if (sec < 60) return $"{Math.Round(sec)}s ago";
            if (sec < 3600) return $"{Math.Round(sec / 60)}m ago";
            return $"{Math.Round(sec / 3600)}h ago";
        }
    }

    [RelayCommand]
    public void SendFiles()
    {
        SendRequested?.Invoke(Device);
    }

    [RelayCommand]
    public void PairDevice()
    {
        PairRequested?.Invoke(Device);
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

