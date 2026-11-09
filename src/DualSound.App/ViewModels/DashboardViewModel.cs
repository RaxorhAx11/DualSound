using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DualSound.Contracts.Enums;
using DualSound.Contracts.Events;
using DualSound.Contracts.Models;
using DualSound.Core.Services;
using DualSound.Core.ViewModels;
using Microsoft.Extensions.Logging;

namespace DualSound.App.ViewModels;

/// <summary>
/// Simplified, clean ViewModel for the DualSound Dashboard.
/// Displays only the essential audio information: TV/HDMI output card,
/// Home Theatre/3.5mm output card, and the current Windows default playback device.
/// </summary>
public sealed partial class DashboardViewModel : ViewModelBase
{
    private readonly IAudioDeviceDiscoveryService _discoveryService;
    private readonly IAudioDeviceMonitorService _monitorService;
    private readonly ISettingsService _settingsService;
    private readonly INavigationService _navigationService;
    private readonly IDispatcherService _dispatcherService;
    private readonly ILogger<DashboardViewModel> _logger;

    private Task? _currentScanTask;

    // Header State
    [ObservableProperty]
    private string _engineStatus = "Audio Engine Ready";

    // Default Device Section
    [ObservableProperty]
    private AudioDeviceInfo? _defaultDevice;

    // Available devices for selection
    [ObservableProperty]
    private ObservableCollection<AudioDeviceInfo> _availableHdmiDevices = [];

    [ObservableProperty]
    private ObservableCollection<AudioDeviceInfo> _availableAnalogDevices = [];

    [ObservableProperty]
    private ObservableCollection<AudioDeviceInfo> _allPlaybackDevices = [];

    // TV / HDMI Output Card
    [ObservableProperty]
    private AudioDeviceInfo? _selectedHdmiDevice;

    [ObservableProperty]
    private string _hdmiConnectionAvailability = "No device selected";

    [ObservableProperty]
    private bool _isHdmiSelected;

    // Home Theatre / 3.5 mm Output Card
    [ObservableProperty]
    private AudioDeviceInfo? _selectedAnalogDevice;

    [ObservableProperty]
    private string _analogConnectionAvailability = "No device selected";

    [ObservableProperty]
    private bool _isAnalogSelected;

    public DashboardViewModel(
        IAudioDeviceDiscoveryService discoveryService,
        IAudioDeviceMonitorService monitorService,
        ISettingsService settingsService,
        INavigationService navigationService,
        IDispatcherService dispatcherService,
        ILogger<DashboardViewModel> logger)
    {
        _discoveryService = discoveryService;
        _monitorService = monitorService;
        _settingsService = settingsService;
        _navigationService = navigationService;
        _dispatcherService = dispatcherService;
        _logger = logger;

        Title = "Dashboard";

        _monitorService.DeviceChanged += OnAudioDeviceChanged;

        _ = LoadDashboardAsync();
    }

    [RelayCommand]
    public async Task LoadDashboardAsync()
    {
        if (_currentScanTask != null && !_currentScanTask.IsCompleted)
        {
            await _currentScanTask;
            return;
        }

        _currentScanTask = LoadDashboardInternalAsync();
        await _currentScanTask;
    }

    private async Task LoadDashboardInternalAsync()
    {
        IsBusy = true;
        BusyMessage = "Scanning audio system...";
        EngineStatus = "Scanning audio endpoints...";
        ClearError();

        try
        {
            var includeInactive = _settingsService.CurrentSettings.ShowDisconnectedDevices ||
                                  _settingsService.CurrentSettings.ShowDisabledDevices;

            var devices = await _discoveryService.GetPlaybackDevicesAsync(includeInactive);
            var defaultDev = await _discoveryService.GetDefaultPlaybackDeviceAsync();

            _dispatcherService.Invoke(() =>
            {
                DefaultDevice = defaultDev;

                AllPlaybackDevices.Clear();
                AvailableHdmiDevices.Clear();
                AvailableAnalogDevices.Clear();

                foreach (var device in devices)
                {
                    AllPlaybackDevices.Add(device);

                    // Populate HDMI-capable devices or all devices if none found
                    if (device.DeviceType == AudioDeviceType.HdmiDisplayPort ||
                        device.Name.Contains("HDMI", StringComparison.OrdinalIgnoreCase) ||
                        device.Name.Contains("Display", StringComparison.OrdinalIgnoreCase))
                    {
                        AvailableHdmiDevices.Add(device);
                    }

                    // Populate 3.5mm/analog devices
                    if (device.DeviceType is AudioDeviceType.LineOut35mm or AudioDeviceType.Headphones or AudioDeviceType.Speakers ||
                        device.EnumeratorName.Equals("HDAUDIO", StringComparison.OrdinalIgnoreCase))
                    {
                        AvailableAnalogDevices.Add(device);
                    }
                }

                // If specialized lists are empty, allow selection from all devices so user is never blocked
                if (AvailableHdmiDevices.Count == 0)
                {
                    foreach (var d in devices) AvailableHdmiDevices.Add(d);
                }
                if (AvailableAnalogDevices.Count == 0)
                {
                    foreach (var d in devices) AvailableAnalogDevices.Add(d);
                }

                // Resolve TV / HDMI device from settings or first available
                var savedPrimaryId = _settingsService.CurrentSettings.PreferredPrimaryDeviceId;
                var resolvedHdmi = devices.FirstOrDefault(d => d.Id == savedPrimaryId) ??
                                   devices.FirstOrDefault(d => d.DeviceType == AudioDeviceType.HdmiDisplayPort && d.IsActive) ??
                                   devices.FirstOrDefault(d => d.DeviceType == AudioDeviceType.HdmiDisplayPort);

                // Resolve Home Theatre / 3.5mm device from settings or first available
                var savedSecondaryId = _settingsService.CurrentSettings.PreferredSecondaryDeviceId;
                var resolvedAnalog = devices.FirstOrDefault(d => d.Id == savedSecondaryId) ??
                                     devices.FirstOrDefault(d => d.DeviceType is AudioDeviceType.LineOut35mm or AudioDeviceType.Headphones or AudioDeviceType.Speakers && d.IsActive) ??
                                     devices.FirstOrDefault(d => d.DeviceType is AudioDeviceType.LineOut35mm or AudioDeviceType.Headphones or AudioDeviceType.Speakers);

                SelectedHdmiDevice = resolvedHdmi;
                SelectedAnalogDevice = resolvedAnalog;

                UpdateHdmiStatus();
                UpdateAnalogStatus();

                EngineStatus = "Audio Engine Ready (Offline)";
            });

            _logger.LogInformation("Dashboard loaded successfully with {Count} audio endpoints", devices.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load dashboard data");
            SetError($"Error discovering audio endpoints: {ex.Message}");
            EngineStatus = "Audio Engine Error";
        }
        finally
        {
            IsBusy = false;
            BusyMessage = null;
        }
    }

    partial void OnSelectedHdmiDeviceChanged(AudioDeviceInfo? value)
    {
        UpdateHdmiStatus();
        if (value != null)
        {
            _settingsService.CurrentSettings.PreferredPrimaryDeviceId = value.Id;
            _ = _settingsService.SaveSettingsAsync(_settingsService.CurrentSettings);
        }
    }

    partial void OnSelectedAnalogDeviceChanged(AudioDeviceInfo? value)
    {
        UpdateAnalogStatus();
        if (value != null)
        {
            _settingsService.CurrentSettings.PreferredSecondaryDeviceId = value.Id;
            _ = _settingsService.SaveSettingsAsync(_settingsService.CurrentSettings);
        }
    }

    private void UpdateHdmiStatus()
    {
        IsHdmiSelected = SelectedHdmiDevice != null;

        if (SelectedHdmiDevice == null)
        {
            HdmiConnectionAvailability = "No device selected";
            return;
        }

        HdmiConnectionAvailability = SelectedHdmiDevice.State switch
        {
            AudioDeviceState.NotPresent => "Display Not Detected",
            AudioDeviceState.Unplugged => "Cable Unplugged",
            AudioDeviceState.Disabled => "Disabled in Windows",
            AudioDeviceState.Active when SelectedHdmiDevice.DeviceType == AudioDeviceType.HdmiDisplayPort => "Display Audio Connected",
            _ => "Connection status unavailable"
        };
    }

    private void UpdateAnalogStatus()
    {
        IsAnalogSelected = SelectedAnalogDevice != null;

        if (SelectedAnalogDevice == null)
        {
            AnalogConnectionAvailability = "No device selected";
            return;
        }

        AnalogConnectionAvailability = SelectedAnalogDevice.State switch
        {
            AudioDeviceState.NotPresent => "Hardware Not Detected",
            AudioDeviceState.Unplugged => "3.5 mm Jack Unplugged",
            AudioDeviceState.Disabled => "Disabled in Windows",
            // Strictly follow rule: Do not assume analog jack is physically connected based only on active endpoint.
            // If physical connection status cannot be verified, display "Connection status unavailable".
            AudioDeviceState.Active => "Connection status unavailable",
            _ => "Connection status unavailable"
        };
    }

    [RelayCommand]
    private void NavigateToDevices()
    {
        _navigationService.NavigateTo<AudioDevicesViewModel>();
    }

    private void OnAudioDeviceChanged(object? sender, DeviceChangeEventArgs e)
    {
        _dispatcherService.Invoke(() =>
        {
            _ = LoadDashboardAsync();
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _monitorService.DeviceChanged -= OnAudioDeviceChanged;
        }
        base.Dispose(disposing);
    }
}
