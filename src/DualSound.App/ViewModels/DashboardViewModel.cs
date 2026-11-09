using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
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
/// Production ViewModel for the DualSound Master Dashboard.
/// Orchestrates TV HDMI and Home Theatre 3.5mm outputs, master audio engine state,
/// quick presets selection, independent volume/mute, and live output VU level metering.
/// </summary>
public sealed partial class DashboardViewModel : ViewModelBase
{
    private readonly IAudioEngineService _audioEngine;
    private readonly IAudioPresetService _presetService;
    private readonly IAudioDeviceDiscoveryService _discoveryService;
    private readonly IAudioDeviceMonitorService _monitorService;
    private readonly ISettingsService _settingsService;
    private readonly INavigationService _navigationService;
    private readonly IDispatcherService _dispatcherService;
    private readonly ILogger<DashboardViewModel> _logger;

    private Task? _currentScanTask;
    private Timer? _meterTimer;

    // Master Engine State
    [ObservableProperty]
    private string _engineStatus = "Audio Engine Ready";

    [ObservableProperty]
    private AudioEngineState _engineState = AudioEngineState.Stopped;

    [ObservableProperty]
    private bool _isEngineRunning;

    [ObservableProperty]
    private string _engineActionButtonText = "Start Audio Engine";

    // Presets
    [ObservableProperty]
    private AudioPresetType _activePresetType = AudioPresetType.MovieTheatre;

    [ObservableProperty]
    private string _activePresetName = "Movie Theatre";

    [ObservableProperty]
    private string _activePresetDescription = string.Empty;

    // Default Device Section
    [ObservableProperty]
    private AudioDeviceInfo? _defaultDevice;

    // Available playback endpoints for dropdowns
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

    [ObservableProperty]
    private float _tvVolume = 1.0f;

    [ObservableProperty]
    private bool _isTvMuted;

    [ObservableProperty]
    private float _tvMeterLevelLeft;

    [ObservableProperty]
    private float _tvMeterLevelRight;

    [ObservableProperty]
    private string _formattedTvPeak = "-60.0 dB";

    // Home Theatre / 3.5 mm Output Card
    [ObservableProperty]
    private AudioDeviceInfo? _selectedAnalogDevice;

    [ObservableProperty]
    private string _analogConnectionAvailability = "No device selected";

    [ObservableProperty]
    private bool _isAnalogSelected;

    [ObservableProperty]
    private float _homeTheatreVolume = 1.0f;

    [ObservableProperty]
    private bool _isHomeTheatreMuted;

    [ObservableProperty]
    private float _homeTheatreMeterLevelLeft;

    [ObservableProperty]
    private float _homeTheatreMeterLevelRight;

    [ObservableProperty]
    private string _formattedHomeTheatrePeak = "-60.0 dB";

    public IReadOnlyList<AudioPreset> Presets => _presetService.AvailablePresets;

    public DashboardViewModel(
        IAudioEngineService audioEngine,
        IAudioPresetService presetService,
        IAudioDeviceDiscoveryService discoveryService,
        IAudioDeviceMonitorService monitorService,
        ISettingsService settingsService,
        INavigationService navigationService,
        IDispatcherService dispatcherService,
        ILogger<DashboardViewModel> logger)
    {
        _audioEngine = audioEngine;
        _presetService = presetService;
        _discoveryService = discoveryService;
        _monitorService = monitorService;
        _settingsService = settingsService;
        _navigationService = navigationService;
        _dispatcherService = dispatcherService;
        _logger = logger;

        Title = "Dashboard";

        // Sync initial engine & preset states
        _engineState = _audioEngine.State;
        UpdateEngineStateVisuals(_engineState);

        _activePresetType = _presetService.ActivePresetType;
        _activePresetName = _presetService.ActivePreset.Name;
        _activePresetDescription = _presetService.ActivePreset.Description;

        // Load initial volume settings
        _tvVolume = _settingsService.CurrentSettings.TvBranch.Volume;
        _isTvMuted = _settingsService.CurrentSettings.TvBranch.IsMuted;
        _homeTheatreVolume = _settingsService.CurrentSettings.HomeTheatreBranch.Volume;
        _isHomeTheatreMuted = _settingsService.CurrentSettings.HomeTheatreBranch.IsMuted;

        _audioEngine.StateChanged += OnEngineStateChanged;
        _audioEngine.EngineError += OnEngineError;
        _presetService.PresetChanged += OnPresetChanged;
        _monitorService.DeviceChanged += OnAudioDeviceChanged;

        // Decoupled 30 Hz VU metering timer
        _meterTimer = new Timer(OnMeterTimerTick, null, 33, 33);

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
                }

                // Populate TV/HDMI choices: prioritizing HDMI/Display/TV endpoints first, then other available devices
                var hdmiMatches = devices.Where(d =>
                    d.DeviceType == AudioDeviceType.HdmiDisplayPort ||
                    d.Name.Contains("HDMI", StringComparison.OrdinalIgnoreCase) ||
                    d.Name.Contains("Display", StringComparison.OrdinalIgnoreCase) ||
                    d.Name.Contains("TV", StringComparison.OrdinalIgnoreCase) ||
                    d.AdapterName.Contains("AMD High Definition", StringComparison.OrdinalIgnoreCase) ||
                    d.AdapterName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)).OrderByDescending(d => d.IsActive).ToList();

                var otherHdmiDevices = devices.Except(hdmiMatches).OrderByDescending(d => d.IsActive).ToList();
                foreach (var d in hdmiMatches) AvailableHdmiDevices.Add(d);
                foreach (var d in otherHdmiDevices) AvailableHdmiDevices.Add(d);

                // Populate Home Theatre/3.5mm choices: prioritizing Analog/Realtek/Speakers/Headphones endpoints first
                var analogMatches = devices.Where(d =>
                    d.DeviceType is AudioDeviceType.LineOut35mm or AudioDeviceType.Headphones or AudioDeviceType.Speakers ||
                    d.EnumeratorName.Equals("HDAUDIO", StringComparison.OrdinalIgnoreCase) ||
                    d.AdapterName.Contains("Realtek", StringComparison.OrdinalIgnoreCase)).OrderByDescending(d => d.IsActive).ToList();

                var otherAnalogDevices = devices.Except(analogMatches).OrderByDescending(d => d.IsActive).ToList();
                foreach (var d in analogMatches) AvailableAnalogDevices.Add(d);
                foreach (var d in otherAnalogDevices) AvailableAnalogDevices.Add(d);

                // Resolve TV / HDMI device
                var savedPrimaryId = _settingsService.CurrentSettings.TvBranch?.DeviceId ?? _settingsService.CurrentSettings.PreferredPrimaryDeviceId;
                var resolvedHdmi = devices.FirstOrDefault(d => d.Id == savedPrimaryId) ??
                                   hdmiMatches.FirstOrDefault(d => d.IsActive) ??
                                   hdmiMatches.FirstOrDefault() ??
                                   devices.FirstOrDefault();

                // Resolve Home Theatre / 3.5mm device
                var savedSecondaryId = _settingsService.CurrentSettings.HomeTheatreBranch?.DeviceId ?? _settingsService.CurrentSettings.PreferredSecondaryDeviceId;
                var resolvedAnalog = devices.FirstOrDefault(d => d.Id == savedSecondaryId) ??
                                     analogMatches.FirstOrDefault(d => d.IsActive && d.Id != resolvedHdmi?.Id) ??
                                     analogMatches.FirstOrDefault(d => d.Id != resolvedHdmi?.Id) ??
                                     devices.FirstOrDefault(d => d.Id != resolvedHdmi?.Id) ??
                                     devices.FirstOrDefault();

                SelectedHdmiDevice = resolvedHdmi;
                SelectedAnalogDevice = resolvedAnalog;

                UpdateHdmiStatus();
                UpdateAnalogStatus();
            });

            _logger.LogInformation("Dashboard loaded successfully with {Count} audio endpoints", devices.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load dashboard data");
            SetError($"Error discovering audio endpoints: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            BusyMessage = null;
        }
    }

    [RelayCommand]
    public async Task ToggleEngineAsync()
    {
        ClearError();
        try
        {
            if (IsEngineRunning)
            {
                await _audioEngine.StopAsync();
            }
            else
            {
                // Ensure output devices are assigned in settings before starting
                if (SelectedHdmiDevice != null)
                {
                    _settingsService.CurrentSettings.PreferredPrimaryDeviceId = SelectedHdmiDevice.Id;
                    _settingsService.CurrentSettings.TvBranch.DeviceId = SelectedHdmiDevice.Id;
                    _audioEngine.OutputService.TvBranch.SetDevice(SelectedHdmiDevice.Id);
                }
                if (SelectedAnalogDevice != null)
                {
                    _settingsService.CurrentSettings.PreferredSecondaryDeviceId = SelectedAnalogDevice.Id;
                    _settingsService.CurrentSettings.HomeTheatreBranch.DeviceId = SelectedAnalogDevice.Id;
                    _audioEngine.OutputService.HomeTheatreBranch.SetDevice(SelectedAnalogDevice.Id);
                }

                await _settingsService.SaveSettingsAsync(_settingsService.CurrentSettings);
                await _audioEngine.StartAsync();
            }
        }
        catch (Exception ex)
        {
            SetError($"Engine Error: {ex.Message}");
        }
    }

    [RelayCommand]
    public void SelectPreset(AudioPresetType type)
    {
        _presetService.ApplyPreset(type);
    }

    [RelayCommand]
    public void ToggleTvMute()
    {
        IsTvMuted = !IsTvMuted;
    }

    [RelayCommand]
    public void ToggleHomeTheatreMute()
    {
        IsHomeTheatreMuted = !IsHomeTheatreMuted;
    }

    partial void OnTvVolumeChanged(float value)
    {
        _audioEngine.OutputService.TvBranch.Volume = value;
        _settingsService.CurrentSettings.TvBranch.Volume = value;
        _ = _settingsService.SaveSettingsAsync(_settingsService.CurrentSettings);
    }

    partial void OnIsTvMutedChanged(bool value)
    {
        _audioEngine.OutputService.TvBranch.IsMuted = value;
        _settingsService.CurrentSettings.TvBranch.IsMuted = value;
        _ = _settingsService.SaveSettingsAsync(_settingsService.CurrentSettings);
    }

    partial void OnHomeTheatreVolumeChanged(float value)
    {
        _audioEngine.OutputService.HomeTheatreBranch.Volume = value;
        _settingsService.CurrentSettings.HomeTheatreBranch.Volume = value;
        _ = _settingsService.SaveSettingsAsync(_settingsService.CurrentSettings);
    }

    partial void OnIsHomeTheatreMutedChanged(bool value)
    {
        _audioEngine.OutputService.HomeTheatreBranch.IsMuted = value;
        _settingsService.CurrentSettings.HomeTheatreBranch.IsMuted = value;
        _ = _settingsService.SaveSettingsAsync(_settingsService.CurrentSettings);
    }

    partial void OnSelectedHdmiDeviceChanged(AudioDeviceInfo? value)
    {
        UpdateHdmiStatus();
        if (value != null)
        {
            _settingsService.CurrentSettings.PreferredPrimaryDeviceId = value.Id;
            _settingsService.CurrentSettings.TvBranch.DeviceId = value.Id;
            _audioEngine.OutputService.TvBranch.SetDevice(value.Id);
            _ = _settingsService.SaveSettingsAsync(_settingsService.CurrentSettings);
        }
    }

    partial void OnSelectedAnalogDeviceChanged(AudioDeviceInfo? value)
    {
        UpdateAnalogStatus();
        if (value != null)
        {
            _settingsService.CurrentSettings.PreferredSecondaryDeviceId = value.Id;
            _settingsService.CurrentSettings.HomeTheatreBranch.DeviceId = value.Id;
            _audioEngine.OutputService.HomeTheatreBranch.SetDevice(value.Id);
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
            AudioDeviceState.Active => "Connection status unavailable",
            _ => "Connection status unavailable"
        };
    }

    private void OnEngineStateChanged(object? sender, AudioEngineState newState)
    {
        _dispatcherService.Invoke(() =>
        {
            EngineState = newState;
            UpdateEngineStateVisuals(newState);
        });
    }

    private void UpdateEngineStateVisuals(AudioEngineState state)
    {
        IsEngineRunning = state == AudioEngineState.Running;
        EngineActionButtonText = IsEngineRunning ? "Stop Audio Engine" : "Start Audio Engine";

        EngineStatus = state switch
        {
            AudioEngineState.Stopped => "Audio Engine Stopped (Offline)",
            AudioEngineState.Initializing => "Starting Pipeline...",
            AudioEngineState.Running => "Dual Output Active (TV + Home Theatre)",
            AudioEngineState.Recovering => "Recovering Audio Streams...",
            AudioEngineState.Error => "Engine Error",
            _ => "Audio Engine Ready"
        };
    }

    private void OnEngineError(object? sender, string errorMessage)
    {
        _dispatcherService.Invoke(() => SetError(errorMessage));
    }

    private void OnPresetChanged(object? sender, AudioPreset preset)
    {
        _dispatcherService.Invoke(() =>
        {
            ActivePresetType = preset.PresetType;
            ActivePresetName = preset.Name;
            ActivePresetDescription = preset.Description;
        });
    }

    private void OnMeterTimerTick(object? state)
    {
        if (!IsEngineRunning)
        {
            if (TvMeterLevelLeft > 0f || HomeTheatreMeterLevelLeft > 0f)
            {
                _dispatcherService.Invoke(() =>
                {
                    TvMeterLevelLeft = 0f;
                    TvMeterLevelRight = 0f;
                    FormattedTvPeak = "-60.0 dB";
                    HomeTheatreMeterLevelLeft = 0f;
                    HomeTheatreMeterLevelRight = 0f;
                    FormattedHomeTheatrePeak = "-60.0 dB";
                });
            }
            return;
        }

        try
        {
            var tvLevels = _audioEngine.OutputService.TvBranch.CurrentLevels;
            var htLevels = _audioEngine.OutputService.HomeTheatreBranch.CurrentLevels;

            _dispatcherService.Invoke(() =>
            {
                TvMeterLevelLeft = tvLevels.LeftPeak;
                TvMeterLevelRight = tvLevels.RightPeak;
                FormattedTvPeak = $"{tvLevels.MasterPeakDb:F1} dB";

                HomeTheatreMeterLevelLeft = htLevels.LeftPeak;
                HomeTheatreMeterLevelRight = htLevels.RightPeak;
                FormattedHomeTheatrePeak = $"{htLevels.MasterPeakDb:F1} dB";
            });
        }
        catch { }
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

    [RelayCommand]
    private void NavigateToEqualizer()
    {
        _navigationService.NavigateTo<EqualizerViewModel>();
    }

    [RelayCommand]
    private void NavigateToSync()
    {
        _navigationService.NavigateTo<SynchronizationViewModel>();
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
            _audioEngine.StateChanged -= OnEngineStateChanged;
            _audioEngine.EngineError -= OnEngineError;
            _presetService.PresetChanged -= OnPresetChanged;
            _monitorService.DeviceChanged -= OnAudioDeviceChanged;
            _meterTimer?.Dispose();
        }
        base.Dispose(disposing);
    }
}
