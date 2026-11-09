using System;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DualSound.Contracts.Enums;
using DualSound.Contracts.Models;
using DualSound.Core.Services;
using DualSound.Core.ViewModels;
using Microsoft.Extensions.Logging;

namespace DualSound.App.ViewModels;

/// <summary>
/// Production ViewModel for output latency alignment, millisecond delay lines,
/// clock drift detection, and synchronization telemetry between HDMI TV and 3.5mm Home Theatre.
/// </summary>
public sealed partial class SynchronizationViewModel : ViewModelBase
{
    private readonly IAudioEngineService _audioEngine;
    private readonly IAudioSyncManager _syncManager;
    private readonly ISettingsService _settingsService;
    private readonly IDispatcherService _dispatcherService;
    private readonly ILogger<SynchronizationViewModel> _logger;

    private Timer? _telemetryTimer;
    private bool _isUpdatingInternally = false;

    // Configured Delays (0 ms to 500 ms)
    [ObservableProperty]
    private float _tvDelayMs = 0f;

    [ObservableProperty]
    private float _homeTheatreDelayMs = 0f;

    [ObservableProperty]
    private string _delayDeltaText = "0 ms (Simultaneous)";

    [ObservableProperty]
    private bool _isEngineRunning;

    // TV Sync Telemetry
    [ObservableProperty] private float _tvOccupancyPercent;
    [ObservableProperty] private string _formattedTvOccupancy = "0.0%";
    [ObservableProperty] private long _tvDriftSamples;
    [ObservableProperty] private long _tvUnderrunCount;
    [ObservableProperty] private long _tvOverrunCount;
    [ObservableProperty] private float _tvEstimatedLatencyMs = 20f;
    [ObservableProperty] private string _tvHealthStatus = "Synchronized";
    [ObservableProperty] private bool _isTvSynchronized = true;

    // Home Theatre Sync Telemetry
    [ObservableProperty] private float _homeTheatreOccupancyPercent;
    [ObservableProperty] private string _formattedHomeTheatreOccupancy = "0.0%";
    [ObservableProperty] private long _homeTheatreDriftSamples;
    [ObservableProperty] private long _homeTheatreUnderrunCount;
    [ObservableProperty] private long _homeTheatreOverrunCount;
    [ObservableProperty] private float _homeTheatreEstimatedLatencyMs = 20f;
    [ObservableProperty] private string _homeTheatreHealthStatus = "Synchronized";
    [ObservableProperty] private bool _isHomeTheatreSynchronized = true;

    [ObservableProperty]
    private string? _notificationMessage;

    public SynchronizationViewModel(
        IAudioEngineService audioEngine,
        IAudioSyncManager syncManager,
        ISettingsService settingsService,
        IDispatcherService dispatcherService,
        ILogger<SynchronizationViewModel> logger)
    {
        _audioEngine = audioEngine;
        _syncManager = syncManager;
        _settingsService = settingsService;
        _dispatcherService = dispatcherService;
        _logger = logger;

        Title = "Synchronization";

        // Load saved delay preferences
        _isUpdatingInternally = true;
        TvDelayMs = _settingsService.CurrentSettings.TvBranch.DelayMilliseconds;
        HomeTheatreDelayMs = _settingsService.CurrentSettings.HomeTheatreBranch.DelayMilliseconds;
        _syncManager.SetDelay(AudioOutputBranch.TvHdmi, TvDelayMs);
        _syncManager.SetDelay(AudioOutputBranch.HomeTheatre35mm, HomeTheatreDelayMs);
        _isUpdatingInternally = false;

        UpdateDelayDelta();

        IsEngineRunning = _audioEngine.State == AudioEngineState.Running;
        _audioEngine.StateChanged += OnEngineStateChanged;

        // Poll sync metrics at ~10 Hz (100 ms)
        _telemetryTimer = new Timer(OnTelemetryTimerTick, null, 100, 100);
    }

    partial void OnTvDelayMsChanged(float value)
    {
        if (_isUpdatingInternally) return;

        _syncManager.SetDelay(AudioOutputBranch.TvHdmi, value);
        _settingsService.CurrentSettings.TvBranch.DelayMilliseconds = value;
        _audioEngine.OutputService.TvBranch.DelayMilliseconds = value;
        _ = _settingsService.SaveSettingsAsync(_settingsService.CurrentSettings);
        UpdateDelayDelta();
    }

    partial void OnHomeTheatreDelayMsChanged(float value)
    {
        if (_isUpdatingInternally) return;

        _syncManager.SetDelay(AudioOutputBranch.HomeTheatre35mm, value);
        _settingsService.CurrentSettings.HomeTheatreBranch.DelayMilliseconds = value;
        _audioEngine.OutputService.HomeTheatreBranch.DelayMilliseconds = value;
        _ = _settingsService.SaveSettingsAsync(_settingsService.CurrentSettings);
        UpdateDelayDelta();
    }

    private void UpdateDelayDelta()
    {
        float delta = TvDelayMs - HomeTheatreDelayMs;
        if (MathF.Abs(delta) < 0.5f)
        {
            DelayDeltaText = "0 ms (Perfect Phase Alignment)";
        }
        else if (delta > 0)
        {
            DelayDeltaText = $"+{delta:F0} ms (TV delayed relative to Home Theatre)";
        }
        else
        {
            DelayDeltaText = $"{MathF.Abs(delta):F0} ms (Home Theatre delayed relative to TV)";
        }
    }

    [RelayCommand]
    public void ResetSync()
    {
        _isUpdatingInternally = true;
        try
        {
            TvDelayMs = 0f;
            HomeTheatreDelayMs = 0f;
            _syncManager.Reset();
            _settingsService.CurrentSettings.TvBranch.DelayMilliseconds = 0f;
            _settingsService.CurrentSettings.HomeTheatreBranch.DelayMilliseconds = 0f;
            _audioEngine.OutputService.TvBranch.DelayMilliseconds = 0f;
            _audioEngine.OutputService.HomeTheatreBranch.DelayMilliseconds = 0f;
            _ = _settingsService.SaveSettingsAsync(_settingsService.CurrentSettings);
            UpdateDelayDelta();
            NotificationMessage = "Delay lines reset to 0 ms; buffer counters cleared.";
        }
        finally
        {
            _isUpdatingInternally = false;
        }
    }

    [RelayCommand]
    public void AdjustTvDelay(string deltaStr)
    {
        if (float.TryParse(deltaStr, out float delta))
        {
            TvDelayMs = Math.Clamp(TvDelayMs + delta, 0f, 500f);
        }
    }

    [RelayCommand]
    public void AdjustHomeTheatreDelay(string deltaStr)
    {
        if (float.TryParse(deltaStr, out float delta))
        {
            HomeTheatreDelayMs = Math.Clamp(HomeTheatreDelayMs + delta, 0f, 500f);
        }
    }

    private void OnEngineStateChanged(object? sender, AudioEngineState state)
    {
        _dispatcherService.Invoke(() =>
        {
            IsEngineRunning = state == AudioEngineState.Running;
        });
    }

    private void OnTelemetryTimerTick(object? state)
    {
        try
        {
            var tvStatus = _syncManager.GetStatus(AudioOutputBranch.TvHdmi);
            var htStatus = _syncManager.GetStatus(AudioOutputBranch.HomeTheatre35mm);

            _dispatcherService.Invoke(() =>
            {
                TvOccupancyPercent = tvStatus.BufferOccupancyPercentage;
                FormattedTvOccupancy = $"{tvStatus.BufferOccupancyPercentage:F1}%";
                TvDriftSamples = tvStatus.DriftSamples;
                TvUnderrunCount = tvStatus.UnderrunCount;
                TvOverrunCount = tvStatus.OverrunCount;
                TvEstimatedLatencyMs = tvStatus.EstimatedLatencyMs;
                TvHealthStatus = tvStatus.HealthDescription;
                IsTvSynchronized = tvStatus.IsSynchronized;

                HomeTheatreOccupancyPercent = htStatus.BufferOccupancyPercentage;
                FormattedHomeTheatreOccupancy = $"{htStatus.BufferOccupancyPercentage:F1}%";
                HomeTheatreDriftSamples = htStatus.DriftSamples;
                HomeTheatreUnderrunCount = htStatus.UnderrunCount;
                HomeTheatreOverrunCount = htStatus.OverrunCount;
                HomeTheatreEstimatedLatencyMs = htStatus.EstimatedLatencyMs;
                HomeTheatreHealthStatus = htStatus.HealthDescription;
                IsHomeTheatreSynchronized = htStatus.IsSynchronized;
            });
        }
        catch { }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _audioEngine.StateChanged -= OnEngineStateChanged;
            _telemetryTimer?.Dispose();
        }
        base.Dispose(disposing);
    }
}
