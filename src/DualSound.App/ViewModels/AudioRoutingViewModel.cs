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
/// ViewModel for the Real-Time Audio Capture Engine (Milestone 02).
/// Manages recording device enumeration (e.g. VB-Audio CABLE Output or Microphone),
/// WASAPI capture lifecycle (Start/Stop), real-time VU level monitoring, and bounded buffer health.
/// </summary>
public sealed partial class AudioRoutingViewModel : ViewModelBase
{
    private readonly IAudioCapture _captureEngine;
    private readonly IAudioDeviceDiscoveryService _discoveryService;
    private readonly IAudioDeviceMonitorService _monitorService;
    private readonly ISettingsService _settingsService;
    private readonly IDispatcherService _dispatcherService;
    private readonly ILogger<AudioRoutingViewModel> _logger;

    private Task? _currentRefreshTask;

    // Milestone status information
    public string FeatureName => "Real-Time Audio Capture Engine";

    public string MilestoneDescription =>
        "Milestone 02: Real-Time WASAPI Audio Capture Engine activated.\n" +
        "Dual-output distribution to TV (HDMI) and Home Theatre (3.5 mm) is Coming Soon in subsequent milestones.";

    // Capture Source
    [ObservableProperty]
    private ObservableCollection<AudioDeviceInfo> _availableRecordingDevices = [];

    [ObservableProperty]
    private AudioDeviceInfo? _selectedRecordingDevice;

    [ObservableProperty]
    private AudioDeviceInfo? _defaultRecordingDevice;

    [ObservableProperty]
    private bool _hasNoDevices;

    // Capture Engine Lifecycle
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

    // Real-Time Audio Levels & Metering
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

    // Status Banner
    [ObservableProperty]
    private string? _statusBannerMessage;

    public AudioRoutingViewModel(
        IAudioCapture captureEngine,
        IAudioDeviceDiscoveryService discoveryService,
        IAudioDeviceMonitorService monitorService,
        ISettingsService settingsService,
        IDispatcherService dispatcherService,
        ILogger<AudioRoutingViewModel> logger)
    {
        _captureEngine = captureEngine;
        _discoveryService = discoveryService;
        _monitorService = monitorService;
        _settingsService = settingsService;
        _dispatcherService = dispatcherService;
        _logger = logger;

        Title = "Audio Capture";

        // Hook capture engine events
        _captureEngine.StateChanged += OnCaptureStateChanged;
        _captureEngine.LevelsUpdated += OnCaptureLevelsUpdated;
        _captureEngine.CaptureError += OnCaptureError;
        _monitorService.DeviceChanged += OnAudioDeviceChanged;

        _ = RefreshRecordingDevicesAsync();
    }

    [RelayCommand]
    public async Task RefreshRecordingDevicesAsync()
    {
        if (_currentRefreshTask != null && !_currentRefreshTask.IsCompleted)
        {
            await _currentRefreshTask;
            return;
        }

        _currentRefreshTask = RefreshRecordingDevicesInternalAsync();
        await _currentRefreshTask;
    }

    private async Task RefreshRecordingDevicesInternalAsync()
    {
        IsBusy = true;
        BusyMessage = "Discovering Windows recording endpoints...";
        ClearError();

        try
        {
            var includeInactive = _settingsService.CurrentSettings.ShowDisconnectedDevices ||
                                  _settingsService.CurrentSettings.ShowDisabledDevices;

            var devices = await _discoveryService.GetRecordingDevicesAsync(includeInactive);
            var defaultDev = await _discoveryService.GetDefaultRecordingDeviceAsync();

            _dispatcherService.Invoke(() =>
            {
                AvailableRecordingDevices.Clear();
                foreach (var d in devices)
                {
                    AvailableRecordingDevices.Add(d);
                }

                DefaultRecordingDevice = defaultDev;
                HasNoDevices = AvailableRecordingDevices.Count == 0;

                // Restore preferred device from settings or choose first active/default
                var savedCaptureId = _settingsService.CurrentSettings.PreferredCaptureDeviceId;
                var resolved = devices.FirstOrDefault(d => d.Id == savedCaptureId) ??
                               devices.FirstOrDefault(d => d.IsDefaultRecording && d.IsActive) ??
                               devices.FirstOrDefault(d => d.IsActive) ??
                               devices.FirstOrDefault();

                SelectedRecordingDevice = resolved;
                UpdateCommandStates();
            });

            _logger.LogInformation("Discovered {Count} recording endpoints", devices.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover recording devices");
            SetError($"Error discovering recording endpoints: {ex.Message}");
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
        if (SelectedRecordingDevice == null)
        {
            SetError("Please select a recording endpoint before starting capture.");
            return;
        }

        ClearError();
        StatusBannerMessage = null;

        try
        {
            _captureEngine.SetDevice(SelectedRecordingDevice);
            await _captureEngine.StartAsync(SelectedRecordingDevice.Id);
            StatusBannerMessage = $"Actively capturing from '{SelectedRecordingDevice.Name}'.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initiate audio capture");
            SetError($"Failed to start audio capture: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task StopCaptureAsync()
    {
        ClearError();
        try
        {
            await _captureEngine.StopAsync();
            StatusBannerMessage = "Audio capture stopped.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while stopping audio capture");
            SetError($"Failed to stop audio capture: {ex.Message}");
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
        UpdateCommandStates();
    }

    private void OnCaptureStateChanged(object? sender, AudioCaptureStateChangedEventArgs e)
    {
        _dispatcherService.Invoke(() =>
        {
            CaptureState = e.NewState;
            CaptureStatusText = e.NewState switch
            {
                AudioCaptureState.Stopped => "Ready",
                AudioCaptureState.Starting => "Initializing...",
                AudioCaptureState.Capturing => "Capturing",
                AudioCaptureState.Stopping => "Stopping...",
                AudioCaptureState.Interrupted => "Device Disconnected",
                AudioCaptureState.Error => "Capture Error",
                _ => "Unknown"
            };

            IsCapturing = e.NewState == AudioCaptureState.Capturing;
            UpdateCommandStates();

            if (e.NewState == AudioCaptureState.Capturing)
            {
                FormattedSampleRate = $"{_captureEngine.SourceSampleRate:N0} Hz (Internal: 48,000 Hz)";
                FormattedChannels = _captureEngine.SourceChannels == 1 ? "1 Ch (Mono to Stereo)" : $"{_captureEngine.SourceChannels} Ch (Stereo)";
                FormattedFormat = _captureEngine.SourceFormatDescription;
            }

            if (!string.IsNullOrEmpty(e.Message))
            {
                StatusBannerMessage = e.Message;
            }
        });
    }

    private void OnCaptureLevelsUpdated(object? sender, AudioLevelsEventArgs e)
    {
        _dispatcherService.Invoke(() =>
        {
            var levels = e.Levels;

            LeftMeterLevel = levels.LeftPeak;
            RightMeterLevel = levels.RightPeak;

            LeftPeakDb = levels.LeftPeakDb;
            RightPeakDb = levels.RightPeakDb;
            MasterPeakDb = levels.MasterPeakDb;

            FormattedLeftPeak = FormatDb(levels.LeftPeakDb);
            FormattedRightPeak = FormatDb(levels.RightPeakDb);
            FormattedMasterPeak = FormatDb(levels.MasterPeakDb);

            HasSignal = levels.HasSignal;
            SignalStatusText = levels.HasSignal ? "Signal Detected" : "No Signal Detected";

            BufferOccupancy = _captureEngine.BufferOccupancyPercentage;
            FormattedBufferOccupancy = $"{BufferOccupancy * 100f:F1}%";
            BufferOverflows = _captureEngine.BufferOverflows;
            TotalFramesCaptured = _captureEngine.TotalFramesCaptured;
        });
    }

    private void OnCaptureError(object? sender, AudioCaptureErrorEventArgs e)
    {
        _dispatcherService.Invoke(() =>
        {
            SetError($"Capture engine alert: {e.Message}");
        });
    }

    private void OnAudioDeviceChanged(object? sender, DeviceChangeEventArgs e)
    {
        _dispatcherService.Invoke(() =>
        {
            _ = RefreshRecordingDevicesAsync();
        });
    }

    private void UpdateCommandStates()
    {
        CanStartCapture = CaptureState is AudioCaptureState.Stopped or AudioCaptureState.Error or AudioCaptureState.Interrupted &&
                          SelectedRecordingDevice != null;

        CanStopCapture = CaptureState is AudioCaptureState.Capturing or AudioCaptureState.Starting;
    }

    private static string FormatDb(float db)
    {
        if (db <= -59.5f)
        {
            return "-∞ dB";
        }
        return $"{db:F1} dB";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _captureEngine.StateChanged -= OnCaptureStateChanged;
            _captureEngine.LevelsUpdated -= OnCaptureLevelsUpdated;
            _captureEngine.CaptureError -= OnCaptureError;
            _monitorService.DeviceChanged -= OnAudioDeviceChanged;
        }
        base.Dispose(disposing);
    }
}
