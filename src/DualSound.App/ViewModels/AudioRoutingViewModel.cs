using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DualSound.Audio.Capture;
using DualSound.Contracts.Enums;
using DualSound.Contracts.Events;
using DualSound.Contracts.Models;
using DualSound.Core.Services;
using DualSound.Core.ViewModels;
using Microsoft.Extensions.Logging;

namespace DualSound.App.ViewModels;

/// <summary>
/// Production ViewModel for Audio Routing Matrix and Real-Time Signal Monitoring.
/// Manages Windows recording capture source, simultaneous TV HDMI and Home Theatre 3.5mm outputs,
/// independent branch volumes, enable/disable switches, and multi-channel VU levels.
/// </summary>
public sealed partial class AudioRoutingViewModel : ViewModelBase
{
    private readonly IAudioEngineService _audioEngine;
    private readonly IAudioCapture _captureEngine;
    private readonly IAudioDeviceDiscoveryService _discoveryService;
    private readonly IAudioDeviceMonitorService _monitorService;
    private readonly ISettingsService _settingsService;
    private readonly IDispatcherService _dispatcherService;
    private readonly ILogger<AudioRoutingViewModel> _logger;

    private Task? _currentRefreshTask;

    // Feature and Milestone info
    public string FeatureName => "Real-Time Audio Routing & Capture Matrix";

    public string MilestoneDescription =>
        "V1.0 Complete Matrix: Capture audio from virtual cable (e.g. CABLE Output) and route simultaneously to TV HDMI and Home Theatre 3.5 mm with independent DSP and delay lines.\n(Physical 5.1/7.1 speaker distribution is Coming Soon in extended surround configurations).";

    // 1. Capture Source (Recording Endpoints)
    [ObservableProperty]
    private ObservableCollection<AudioDeviceInfo> _availableRecordingDevices = [];

    [ObservableProperty]
    private AudioDeviceInfo? _selectedRecordingDevice;

    [ObservableProperty]
    private AudioDeviceInfo? _defaultRecordingDevice;

    [ObservableProperty]
    private bool _hasNoDevices;

    // 2. Playback Destinations
    [ObservableProperty]
    private ObservableCollection<AudioDeviceInfo> _availablePlaybackDevices = [];

    [ObservableProperty]
    private AudioDeviceInfo? _selectedTvDevice;

    [ObservableProperty]
    private AudioDeviceInfo? _selectedHomeTheatreDevice;

    // TV Branch Output Controls
    [ObservableProperty]
    private bool _isTvEnabled = true;

    [ObservableProperty]
    private float _tvVolume = 1.0f;

    [ObservableProperty]
    private bool _isTvMuted;

    [ObservableProperty]
    private string _tvPeakDbfs = "-60.0 dB";

    // Home Theatre Branch Output Controls
    [ObservableProperty]
    private bool _isHomeTheatreEnabled = true;

    [ObservableProperty]
    private float _homeTheatreVolume = 1.0f;

    [ObservableProperty]
    private bool _isHomeTheatreMuted;

    [ObservableProperty]
    private string _homeTheatrePeakDbfs = "-60.0 dB";

    // Master Engine Lifecycle
    [ObservableProperty]
    private AudioEngineState _engineState = AudioEngineState.Stopped;

    [ObservableProperty]
    private AudioCaptureState _captureState = AudioCaptureState.Stopped;

    [ObservableProperty]
    private string _captureStatusText = "Ready";

    [ObservableProperty]
    private bool _isCapturing;

    [ObservableProperty]
    private bool _canStartCapture = true;

    [ObservableProperty]
    private bool _canStopCapture;

    // Capture VU Levels & Metering
    [ObservableProperty]
    private float _leftMeterLevel;

    [ObservableProperty]
    private float _rightMeterLevel;

    [ObservableProperty]
    private float _leftPeakDb = -60f;

    [ObservableProperty]
    private float _rightPeakDb = -60f;

    [ObservableProperty]
    private float _masterPeakDb = -60f;

    [ObservableProperty]
    private string _formattedLeftPeak = "-60.0 dB";

    [ObservableProperty]
    private string _formattedRightPeak = "-60.0 dB";

    [ObservableProperty]
    private string _formattedMasterPeak = "-60.0 dB";

    [ObservableProperty]
    private bool _hasSignal;

    [ObservableProperty]
    private string _signalStatusText = "No Signal Detected";

    // Stream & Buffer Health Statistics
    [ObservableProperty]
    private string _formattedSampleRate = "48,000 Hz";

    [ObservableProperty]
    private string _formattedChannels = "2 Channels (Stereo)";

    [ObservableProperty]
    private string _formattedFormat = "32-bit Float • 48 kHz";

    [ObservableProperty]
    private float _bufferOccupancy;

    [ObservableProperty]
    private string _formattedBufferOccupancy = "0.0%";

    [ObservableProperty]
    private long _bufferOverflows;

    [ObservableProperty]
    private long _totalFramesCaptured;

    [ObservableProperty]
    private string? _statusBannerMessage;

    public AudioRoutingViewModel(
        IAudioEngineService audioEngine,
        IAudioCapture captureEngine,
        IAudioDeviceDiscoveryService discoveryService,
        IAudioDeviceMonitorService monitorService,
        ISettingsService settingsService,
        IDispatcherService dispatcherService,
        ILogger<AudioRoutingViewModel> logger)
    {
        _audioEngine = audioEngine;
        _captureEngine = captureEngine;
        _discoveryService = discoveryService;
        _monitorService = monitorService;
        _settingsService = settingsService;
        _dispatcherService = dispatcherService;
        _logger = logger;

        Title = "Audio Routing";

        // Initial volume & enable state
        _tvVolume = _settingsService.CurrentSettings.TvBranch.Volume;
        _isTvEnabled = _settingsService.CurrentSettings.TvBranch.IsEnabled;
        _isTvMuted = _settingsService.CurrentSettings.TvBranch.IsMuted;

        _homeTheatreVolume = _settingsService.CurrentSettings.HomeTheatreBranch.Volume;
        _isHomeTheatreEnabled = _settingsService.CurrentSettings.HomeTheatreBranch.IsEnabled;
        _isHomeTheatreMuted = _settingsService.CurrentSettings.HomeTheatreBranch.IsMuted;

        // Wire engine events
        _captureEngine.StateChanged += OnCaptureStateChanged;
        _captureEngine.LevelsUpdated += OnLevelsUpdated;
        _captureEngine.CaptureError += OnCaptureError;
        _monitorService.DeviceChanged += OnAudioDeviceChanged;
        _audioEngine.StateChanged += OnEngineStateChanged;

        _ = RefreshAllDevicesAsync();
    }

    [RelayCommand]
    public async Task RefreshAllDevicesAsync()
    {
        if (_currentRefreshTask != null && !_currentRefreshTask.IsCompleted)
        {
            await _currentRefreshTask;
            return;
        }

        _currentRefreshTask = RefreshAllDevicesInternalAsync();
        await _currentRefreshTask;
    }

    [RelayCommand]
    public Task RefreshRecordingDevicesAsync() => RefreshAllDevicesAsync();

    private async Task RefreshAllDevicesInternalAsync()
    {
        IsBusy = true;
        BusyMessage = "Enumerating audio endpoints...";
        ClearError();

        try
        {
            var includeInactive = _settingsService.CurrentSettings.ShowDisconnectedDevices ||
                                  _settingsService.CurrentSettings.ShowDisabledDevices;

            var recDevices = await _discoveryService.GetRecordingDevicesAsync(includeInactive);
            var defaultRec = await _discoveryService.GetDefaultRecordingDeviceAsync();

            var playDevices = await _discoveryService.GetPlaybackDevicesAsync(includeInactive);

            _dispatcherService.Invoke(() =>
            {
                // Recording Devices
                AvailableRecordingDevices.Clear();
                foreach (var dev in recDevices) AvailableRecordingDevices.Add(dev);
                DefaultRecordingDevice = defaultRec;
                HasNoDevices = AvailableRecordingDevices.Count == 0;

                var savedCaptureId = _settingsService.CurrentSettings.PreferredCaptureDeviceId;
                SelectedRecordingDevice = AvailableRecordingDevices.FirstOrDefault(d => d.Id == savedCaptureId) ??
                                          AvailableRecordingDevices.FirstOrDefault(d => d.IsDefaultRecording) ??
                                          AvailableRecordingDevices.FirstOrDefault(d => d.IsActive) ??
                                          AvailableRecordingDevices.FirstOrDefault();

                // Playback Devices
                AvailablePlaybackDevices.Clear();
                foreach (var dev in playDevices) AvailablePlaybackDevices.Add(dev);

                var savedTvId = _settingsService.CurrentSettings.PreferredPrimaryDeviceId;
                SelectedTvDevice = AvailablePlaybackDevices.FirstOrDefault(d => d.Id == savedTvId) ??
                                   AvailablePlaybackDevices.FirstOrDefault(d => d.DeviceType == AudioDeviceType.HdmiDisplayPort && d.IsActive) ??
                                   AvailablePlaybackDevices.FirstOrDefault();

                var savedHtId = _settingsService.CurrentSettings.PreferredSecondaryDeviceId;
                SelectedHomeTheatreDevice = AvailablePlaybackDevices.FirstOrDefault(d => d.Id == savedHtId) ??
                                            AvailablePlaybackDevices.FirstOrDefault(d => d.DeviceType is AudioDeviceType.LineOut35mm or AudioDeviceType.Headphones or AudioDeviceType.Speakers && d.IsActive) ??
                                            AvailablePlaybackDevices.FirstOrDefault();
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh audio routing endpoints");
            SetError($"Failed to enumerate endpoints: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            BusyMessage = null;
        }
    }

    [RelayCommand]
    public async Task StartCaptureAsync()
    {
        ClearError();
        StatusBannerMessage = null;

        try
        {
            if (SelectedRecordingDevice != null)
            {
                _captureEngine.SetDevice(SelectedRecordingDevice);
            }

            await _audioEngine.StartAsync();
            StatusBannerMessage = "Master audio engine and capture running.";
        }
        catch (Exception ex)
        {
            SetError($"Failed to start routing: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task StopCaptureAsync()
    {
        ClearError();
        try
        {
            await _audioEngine.StopAsync();
            StatusBannerMessage = "Audio routing stopped.";
        }
        catch (Exception ex)
        {
            SetError($"Failed to stop routing: {ex.Message}");
        }
    }

    partial void OnSelectedRecordingDeviceChanged(AudioDeviceInfo? value)
    {
        if (value != null)
        {
            _captureEngine.SetDevice(value);
            _settingsService.CurrentSettings.PreferredCaptureDeviceId = value.Id;
            _ = _settingsService.SaveSettingsAsync(_settingsService.CurrentSettings);
        }
    }

    partial void OnSelectedTvDeviceChanged(AudioDeviceInfo? value)
    {
        if (value != null)
        {
            _settingsService.CurrentSettings.PreferredPrimaryDeviceId = value.Id;
            _settingsService.CurrentSettings.TvBranch.DeviceId = value.Id;
            _audioEngine.OutputService.TvBranch.SetDevice(value.Id);
            _ = _settingsService.SaveSettingsAsync(_settingsService.CurrentSettings);
        }
    }

    partial void OnSelectedHomeTheatreDeviceChanged(AudioDeviceInfo? value)
    {
        if (value != null)
        {
            _settingsService.CurrentSettings.PreferredSecondaryDeviceId = value.Id;
            _settingsService.CurrentSettings.HomeTheatreBranch.DeviceId = value.Id;
            _audioEngine.OutputService.HomeTheatreBranch.SetDevice(value.Id);
            _ = _settingsService.SaveSettingsAsync(_settingsService.CurrentSettings);
        }
    }

    partial void OnTvVolumeChanged(float value)
    {
        _audioEngine.OutputService.TvBranch.Volume = value;
        _settingsService.CurrentSettings.TvBranch.Volume = value;
        _ = _settingsService.SaveSettingsAsync(_settingsService.CurrentSettings);
    }

    partial void OnIsTvEnabledChanged(bool value)
    {
        _audioEngine.OutputService.TvBranch.IsEnabled = value;
        _settingsService.CurrentSettings.TvBranch.IsEnabled = value;
        _ = _settingsService.SaveSettingsAsync(_settingsService.CurrentSettings);
    }

    partial void OnHomeTheatreVolumeChanged(float value)
    {
        _audioEngine.OutputService.HomeTheatreBranch.Volume = value;
        _settingsService.CurrentSettings.HomeTheatreBranch.Volume = value;
        _ = _settingsService.SaveSettingsAsync(_settingsService.CurrentSettings);
    }

    partial void OnIsHomeTheatreEnabledChanged(bool value)
    {
        _audioEngine.OutputService.HomeTheatreBranch.IsEnabled = value;
        _settingsService.CurrentSettings.HomeTheatreBranch.IsEnabled = value;
        _ = _settingsService.SaveSettingsAsync(_settingsService.CurrentSettings);
    }

    private void OnEngineStateChanged(object? sender, AudioEngineState state)
    {
        _dispatcherService.Invoke(() =>
        {
            EngineState = state;
            IsCapturing = state == AudioEngineState.Running;
            CanStartCapture = state is AudioEngineState.Stopped or AudioEngineState.Error;
            CanStopCapture = state is AudioEngineState.Running or AudioEngineState.Initializing;
        });
    }

    private void OnCaptureStateChanged(object? sender, AudioCaptureStateChangedEventArgs e)
    {
        _dispatcherService.Invoke(() =>
        {
            CaptureState = e.NewState;
            CaptureStatusText = e.NewState switch
            {
                AudioCaptureState.Stopped => "Stopped",
                AudioCaptureState.Starting => "Starting...",
                AudioCaptureState.Capturing => "Active (Capturing)",
                AudioCaptureState.Stopping => "Stopping...",
                AudioCaptureState.Interrupted => "Interrupted (Reconnecting...)",
                AudioCaptureState.Error => "Capture Error",
                _ => "Unknown"
            };

            FormattedSampleRate = $"{_captureEngine.SourceSampleRate:N0} Hz";
            FormattedChannels = $"{_captureEngine.SourceChannels} Channels";
            FormattedFormat = _captureEngine.SourceFormatDescription;
        });
    }

    private void OnLevelsUpdated(object? sender, AudioLevelsEventArgs e)
    {
        _dispatcherService.Invoke(() =>
        {
            LeftMeterLevel = e.Levels.LeftPeak;
            RightMeterLevel = e.Levels.RightPeak;
            LeftPeakDb = e.Levels.LeftPeakDb;
            RightPeakDb = e.Levels.RightPeakDb;
            MasterPeakDb = e.Levels.MasterPeakDb;

            FormattedLeftPeak = $"{e.Levels.LeftPeakDb:F1} dB";
            FormattedRightPeak = $"{e.Levels.RightPeakDb:F1} dB";
            FormattedMasterPeak = $"{e.Levels.MasterPeakDb:F1} dB";

            HasSignal = e.Levels.HasSignal;
            SignalStatusText = e.Levels.HasSignal ? "Signal Active" : "Silence / Noise Gate Active";

            BufferOccupancy = _captureEngine.BufferOccupancyPercentage;
            FormattedBufferOccupancy = $"{_captureEngine.BufferOccupancyPercentage:F1}%";
            BufferOverflows = _captureEngine.BufferOverflows;
            TotalFramesCaptured = _captureEngine.TotalFramesCaptured;

            // Update output level meters
            TvPeakDbfs = $"{_audioEngine.OutputService.TvBranch.CurrentLevels.MasterPeakDb:F1} dB";
            HomeTheatrePeakDbfs = $"{_audioEngine.OutputService.HomeTheatreBranch.CurrentLevels.MasterPeakDb:F1} dB";
        });
    }

    private void OnCaptureError(object? sender, AudioCaptureErrorEventArgs e)
    {
        _dispatcherService.Invoke(() => SetError(e.Message));
    }

    private void OnAudioDeviceChanged(object? sender, DeviceChangeEventArgs e)
    {
        _dispatcherService.Invoke(() =>
        {
            _ = RefreshAllDevicesAsync();
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _captureEngine.StateChanged -= OnCaptureStateChanged;
            _captureEngine.LevelsUpdated -= OnLevelsUpdated;
            _captureEngine.CaptureError -= OnCaptureError;
            _monitorService.DeviceChanged -= OnAudioDeviceChanged;
            _audioEngine.StateChanged -= OnEngineStateChanged;
        }
        base.Dispose(disposing);
    }
}
