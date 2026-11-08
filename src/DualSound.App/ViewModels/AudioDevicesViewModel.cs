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
/// ViewModel for audio device enumeration, filtering, inspection, and real-time lifecycle tracking.
/// </summary>
public sealed partial class AudioDevicesViewModel : ViewModelBase
{
    private readonly IAudioDeviceDiscoveryService _discoveryService;
    private readonly IAudioDeviceMonitorService _monitorService;
    private readonly ISettingsService _settingsService;
    private readonly IDispatcherService _dispatcherService;
    private readonly ILogger<AudioDevicesViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<AudioDeviceInfo> _devices = [];

    [ObservableProperty]
    private ObservableCollection<AudioDeviceInfo> _filteredDevices = [];

    [ObservableProperty]
    private AudioDeviceInfo? _selectedDevice;

    [ObservableProperty]
    private AudioDeviceInfo? _defaultDevice;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private string _selectedStateFilter = "All States";

    [ObservableProperty]
    private string _selectedTypeFilter = "All Types";

    [ObservableProperty]
    private bool _autoRefreshEnabled = true;

    [ObservableProperty]
    private string? _statusNotification;

    [ObservableProperty]
    private int _activeDeviceCount;

    [ObservableProperty]
    private int _totalDeviceCount;

    public string[] StateFilters { get; } = ["All States", "Active Only", "Unplugged", "Disabled", "Not Present"];
    public string[] TypeFilters { get; } = ["All Types", "HDMI / DisplayPort", "3.5 mm / Analog", "USB Audio", "Bluetooth Audio", "Virtual Audio"];

    private Task? _currentRefreshTask;

    public AudioDevicesViewModel(
        IAudioDeviceDiscoveryService discoveryService,
        IAudioDeviceMonitorService monitorService,
        ISettingsService settingsService,
        IDispatcherService dispatcherService,
        ILogger<AudioDevicesViewModel> logger)
    {
        _discoveryService = discoveryService;
        _monitorService = monitorService;
        _settingsService = settingsService;
        _dispatcherService = dispatcherService;
        _logger = logger;

        Title = "Audio Endpoints";

        _autoRefreshEnabled = _settingsService.CurrentSettings.AutoRefreshDevices;
        _monitorService.DeviceChanged += OnAudioDeviceChanged;

        if (_autoRefreshEnabled && !_monitorService.IsMonitoring)
        {
            try
            {
                _monitorService.StartMonitoring();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not start audio device monitor at initialization");
            }
        }

        // Initial load
        _currentRefreshTask = RefreshDevicesInternalAsync();
    }

    [RelayCommand]
    public async Task RefreshDevicesAsync()
    {
        if (_currentRefreshTask != null && !_currentRefreshTask.IsCompleted)
        {
            await _currentRefreshTask;
            return;
        }

        _currentRefreshTask = RefreshDevicesInternalAsync();
        await _currentRefreshTask;
    }

    private async Task RefreshDevicesInternalAsync()
    {
        IsBusy = true;
        BusyMessage = "Enumerating Windows audio endpoints...";
        ClearError();

        try
        {
            var includeInactive = _settingsService.CurrentSettings.ShowDisconnectedDevices ||
                                  _settingsService.CurrentSettings.ShowDisabledDevices;

            var deviceList = await _discoveryService.GetPlaybackDevicesAsync(includeInactive);
            var defaultDev = await _discoveryService.GetDefaultPlaybackDeviceAsync();

            _dispatcherService.Invoke(() =>
            {
                Devices.Clear();
                foreach (var dev in deviceList)
                {
                    Devices.Add(dev);
                }

                DefaultDevice = defaultDev;
                TotalDeviceCount = Devices.Count;
                ActiveDeviceCount = Devices.Count(d => d.IsActive);

                ApplyFilter();

                // Maintain selection or select first
                if (SelectedDevice != null)
                {
                    SelectedDevice = Devices.FirstOrDefault(d => d.Id == SelectedDevice.Id) ?? Devices.FirstOrDefault();
                }
                else
                {
                    SelectedDevice = Devices.FirstOrDefault(d => d.IsDefaultPlayback) ?? Devices.FirstOrDefault();
                }
            });

            _logger.LogInformation("Discovered {Total} audio devices ({Active} active)", TotalDeviceCount, ActiveDeviceCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh audio devices");
            SetError($"Error discovering audio devices: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            BusyMessage = null;
        }
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchQuery = string.Empty;
    }

    [RelayCommand]
    private void ToggleAutoRefresh()
    {
        AutoRefreshEnabled = !AutoRefreshEnabled;
        _settingsService.CurrentSettings.AutoRefreshDevices = AutoRefreshEnabled;
        _ = _settingsService.SaveSettingsAsync(_settingsService.CurrentSettings);

        if (AutoRefreshEnabled)
        {
            try
            {
                _monitorService.StartMonitoring();
                StatusNotification = "Real-time device monitoring enabled";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to start monitoring");
            }
        }
        else
        {
            _monitorService.StopMonitoring();
            StatusNotification = "Real-time device monitoring disabled";
        }
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();
    partial void OnSelectedStateFilterChanged(string value) => ApplyFilter();
    partial void OnSelectedTypeFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var query = SearchQuery?.Trim() ?? string.Empty;

        var items = Devices.AsEnumerable();

        if (!string.IsNullOrEmpty(query))
        {
            items = items.Where(d =>
                d.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                d.AdapterName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                d.DeviceTypeDescription.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                d.Id.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        if (SelectedStateFilter == "Active Only")
        {
            items = items.Where(d => d.State == AudioDeviceState.Active);
        }
        else if (SelectedStateFilter == "Unplugged")
        {
            items = items.Where(d => d.State == AudioDeviceState.Unplugged);
        }
        else if (SelectedStateFilter == "Disabled")
        {
            items = items.Where(d => d.State == AudioDeviceState.Disabled);
        }
        else if (SelectedStateFilter == "Not Present")
        {
            items = items.Where(d => d.State == AudioDeviceState.NotPresent);
        }

        if (SelectedTypeFilter == "HDMI / DisplayPort")
        {
            items = items.Where(d => d.DeviceType == AudioDeviceType.HdmiDisplayPort);
        }
        else if (SelectedTypeFilter == "3.5 mm / Analog")
        {
            items = items.Where(d => d.DeviceType is AudioDeviceType.LineOut35mm or AudioDeviceType.Headphones or AudioDeviceType.Speakers);
        }
        else if (SelectedTypeFilter == "USB Audio")
        {
            items = items.Where(d => d.DeviceType == AudioDeviceType.UsbAudio);
        }
        else if (SelectedTypeFilter == "Bluetooth Audio")
        {
            items = items.Where(d => d.DeviceType == AudioDeviceType.BluetoothAudio);
        }
        else if (SelectedTypeFilter == "Virtual Audio")
        {
            items = items.Where(d => d.DeviceType == AudioDeviceType.VirtualAudio);
        }

        FilteredDevices = new ObservableCollection<AudioDeviceInfo>(items);
    }

    private void OnAudioDeviceChanged(object? sender, DeviceChangeEventArgs e)
    {
        _dispatcherService.Invoke(() =>
        {
            StatusNotification = e.ChangeType switch
            {
                DeviceChangeType.DeviceAdded => "Audio device connected",
                DeviceChangeType.DeviceRemoved => "Audio device disconnected",
                DeviceChangeType.DeviceStateChanged => $"Audio device status updated: {e.NewState}",
                DeviceChangeType.DefaultDeviceChanged => "Default Windows playback device changed",
                _ => "Audio endpoints updated"
            };

            // Automatically refresh device list to reflect the change
            _ = RefreshDevicesAsync();
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
