using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DualSound.Contracts.Enums;
using DualSound.Contracts.Models;
using DualSound.Core.Services;
using DualSound.Core.ViewModels;
using Microsoft.Extensions.Logging;

namespace DualSound.App.ViewModels;

/// <summary>
/// Dashboard ViewModel providing high-level overview of audio endpoints, default playback route,
/// and target hardware setup readiness (HDMI TV + 3.5mm Home Theatre).
/// </summary>
public sealed partial class DashboardViewModel : ViewModelBase
{
    private readonly IAudioDeviceDiscoveryService _discoveryService;
    private readonly INavigationService _navigationService;
    private readonly IDispatcherService _dispatcherService;
    private readonly ILogger<DashboardViewModel> _logger;

    [ObservableProperty]
    private AudioDeviceInfo? _defaultDevice;

    [ObservableProperty]
    private AudioDeviceInfo? _hdmiDevice;

    [ObservableProperty]
    private AudioDeviceInfo? _analog35mmDevice;

    [ObservableProperty]
    private int _totalEndpointsCount;

    [ObservableProperty]
    private int _activeEndpointsCount;

    [ObservableProperty]
    private int _hdmiEndpointsCount;

    [ObservableProperty]
    private int _analogEndpointsCount;

    [ObservableProperty]
    private int _usbEndpointsCount;

    [ObservableProperty]
    private int _bluetoothEndpointsCount;

    [ObservableProperty]
    private int _virtualEndpointsCount;

    [ObservableProperty]
    private bool _isHdmiReady;

    [ObservableProperty]
    private bool _is35mmReady;

    [ObservableProperty]
    private ObservableCollection<AudioDeviceInfo> _activeDevices = [];

    public DashboardViewModel(
        IAudioDeviceDiscoveryService discoveryService,
        INavigationService navigationService,
        IDispatcherService dispatcherService,
        ILogger<DashboardViewModel> logger)
    {
        _discoveryService = discoveryService;
        _navigationService = navigationService;
        _dispatcherService = dispatcherService;
        _logger = logger;

        Title = "Dashboard";

        _ = LoadDashboardAsync();
    }

    [RelayCommand]
    public async Task LoadDashboardAsync()
    {
        IsBusy = true;
        BusyMessage = "Scanning audio system...";
        ClearError();

        try
        {
            var devices = await _discoveryService.GetPlaybackDevicesAsync(includeInactive: true);
            var defaultDev = await _discoveryService.GetDefaultPlaybackDeviceAsync();

            _dispatcherService.Invoke(() =>
            {
                DefaultDevice = defaultDev;
                TotalEndpointsCount = devices.Count;

                var activeList = devices.Where(d => d.IsActive).ToList();
                ActiveEndpointsCount = activeList.Count;

                ActiveDevices.Clear();
                foreach (var d in activeList)
                {
                    ActiveDevices.Add(d);
                }

                HdmiEndpointsCount = devices.Count(d => d.DeviceType == AudioDeviceType.HdmiDisplayPort);
                AnalogEndpointsCount = devices.Count(d => d.DeviceType is AudioDeviceType.LineOut35mm or AudioDeviceType.Headphones or AudioDeviceType.Speakers);
                UsbEndpointsCount = devices.Count(d => d.DeviceType == AudioDeviceType.UsbAudio);
                BluetoothEndpointsCount = devices.Count(d => d.DeviceType == AudioDeviceType.BluetoothAudio);
                VirtualEndpointsCount = devices.Count(d => d.DeviceType == AudioDeviceType.VirtualAudio);

                // Hardware setup analysis:
                // Primary: TV through HDMI
                HdmiDevice = activeList.FirstOrDefault(d => d.DeviceType == AudioDeviceType.HdmiDisplayPort);
                IsHdmiReady = HdmiDevice != null;

                // Secondary: Home Theatre through 3.5mm jack
                Analog35mmDevice = activeList.FirstOrDefault(d => d.DeviceType is AudioDeviceType.LineOut35mm or AudioDeviceType.Headphones or AudioDeviceType.Speakers);
                Is35mmReady = Analog35mmDevice != null;
            });

            _logger.LogInformation("Dashboard loaded: {ActiveCount} active audio devices", ActiveEndpointsCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load dashboard data");
            SetError($"Failed to query audio devices: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            BusyMessage = null;
        }
    }

    [RelayCommand]
    private void NavigateToDevices()
    {
        _navigationService.NavigateTo<AudioDevicesViewModel>();
    }

    [RelayCommand]
    private void NavigateToRouting()
    {
        _navigationService.NavigateTo<AudioRoutingViewModel>();
    }
}
