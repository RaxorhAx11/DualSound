using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
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
/// ViewModel for Automatic Sync Calibration (acoustic time-of-flight measurement via microphone)
/// and Live Sync Monitoring (continuous hardware clock drift detection and smooth compensation).
/// </summary>
public sealed partial class AutomaticSyncViewModel : ViewModelBase
{
    private readonly IAcousticCalibrationService _calibrationService;
    private readonly ILiveSyncMonitoringService _liveSyncService;
    private readonly IAudioSyncManager _syncManager;
    private readonly ISettingsService _settingsService;
    private readonly IAudioEngineService _audioEngine;
    private readonly INavigationService _navigationService;
    private readonly IDispatcherService _dispatcherService;
    private readonly ILogger<AutomaticSyncViewModel> _logger;

    // Snapshot of working delays prior to calibration
    private float _snapshotTvDelayMs;
    private float _snapshotHomeTheatreDelayMs;
    private bool _hasSnapshot;

    // Current Active Delays
    [ObservableProperty] private float _tvDelayMs;
    [ObservableProperty] private float _homeTheatreDelayMs;
    [ObservableProperty] private string _delayDeltaText = "0 ms (Simultaneous)";

    // Microphone & Environment
    [ObservableProperty] private ObservableCollection<AudioDeviceInfo> _availableMicrophones = [];
    [ObservableProperty] private AudioDeviceInfo? _selectedMicrophone;
    [ObservableProperty] private bool _isMicrophoneAvailable;
    [ObservableProperty] private bool _isMicrophoneAccessDenied;
    [ObservableProperty] private string _microphoneStatusText = "Scanning for microphones...";

    // Calibration Execution & Progress
    [ObservableProperty] private bool _isCalibrating;
    [ObservableProperty] private bool _isVerifying;
    [ObservableProperty] private float _calibrationProgress;
    [ObservableProperty] private string _calibrationStatusText = "Ready to calibrate acoustic synchronization.";
    [ObservableProperty] private string? _calibrationDetailedMessage;
    [ObservableProperty] private bool _hasCalibrationResult;
    [ObservableProperty] private bool _canSaveCalibration;
    [ObservableProperty] private bool _canRestorePrevious;

    // Calibration Results
    [ObservableProperty] private float _measuredTvArrivalMs;
    [ObservableProperty] private float _measuredHomeTheatreArrivalMs;
    [ObservableProperty] private float _measuredDifferenceMs;
    [ObservableProperty] private float _recommendedTvDelayMs;
    [ObservableProperty] private float _recommendedHomeTheatreDelayMs;
    [ObservableProperty] private float _confidenceScore;
    [ObservableProperty] private string _confidenceText = "N/A";
    [ObservableProperty] private bool _isConfidenceAcceptable;
    [ObservableProperty] private string? _acousticWarning;
    [ObservableProperty] private string _lastCalibrationTimeText = "Not yet calibrated";

    // Live Sync Monitoring
    [ObservableProperty] private bool _isLiveSyncEnabled;
    [ObservableProperty] private string _liveMonitoringStatusText = "Disabled";
    [ObservableProperty] private string _estimatedDriftText = "0.0 ms";
    [ObservableProperty] private string _totalCorrectionsText = "0";
    [ObservableProperty] private string? _driftLimitationNotice;

    public AutomaticSyncViewModel(
        IAcousticCalibrationService calibrationService,
        ILiveSyncMonitoringService liveSyncService,
        IAudioSyncManager syncManager,
        ISettingsService settingsService,
        IAudioEngineService audioEngine,
        INavigationService navigationService,
        IDispatcherService dispatcherService,
        ILogger<AutomaticSyncViewModel> logger)
    {
        _calibrationService = calibrationService;
        _liveSyncService = liveSyncService;
        _syncManager = syncManager;
        _settingsService = settingsService;
        _audioEngine = audioEngine;
        _navigationService = navigationService;
        _dispatcherService = dispatcherService;
        _logger = logger;

        Title = "Automatic Sync Calibration";

        // Load active delays
        TvDelayMs = _settingsService.CurrentSettings.TvBranch.DelayMilliseconds;
        HomeTheatreDelayMs = _settingsService.CurrentSettings.HomeTheatreBranch.DelayMilliseconds;
        UpdateDelayDelta();

        // Load persisted calibration history
        var lastTime = _settingsService.CurrentSettings.LastCalibrationTimeUtc;
        if (lastTime.HasValue)
        {
            LastCalibrationTimeText = lastTime.Value.ToLocalTime().ToString("g");
            if (_settingsService.CurrentSettings.LastCalibrationConfidence.HasValue)
            {
                ConfidenceScore = _settingsService.CurrentSettings.LastCalibrationConfidence.Value;
                ConfidenceText = $"{ConfidenceScore * 100:F0}%";
                IsConfidenceAcceptable = ConfidenceScore >= 0.60f;
            }
            if (_settingsService.CurrentSettings.LastMeasuredDifferenceMs.HasValue)
            {
                MeasuredDifferenceMs = _settingsService.CurrentSettings.LastMeasuredDifferenceMs.Value;
            }
        }

        // Live Sync Monitoring initialization
        IsLiveSyncEnabled = _liveSyncService.IsEnabled;
        UpdateLiveSyncStatus(_liveSyncService.CurrentStatus);
        _liveSyncService.StatusChanged += OnLiveSyncStatusChanged;

        _calibrationService.ProgressChanged += OnCalibrationProgressChanged;

        // Initialize microphone detection
        _ = RefreshMicrophonesAsync();
    }

    partial void OnIsLiveSyncEnabledChanged(bool value)
    {
        _liveSyncService.IsEnabled = value;
    }

    partial void OnSelectedMicrophoneChanged(AudioDeviceInfo? value)
    {
        if (value != null)
        {
            _settingsService.CurrentSettings.SelectedCalibrationMicrophoneId = value.Id;
            _ = _settingsService.SaveSettingsAsync(_settingsService.CurrentSettings);
            _ = VerifyMicrophoneAccessAsync(value.Id);
        }
    }

    private void UpdateDelayDelta()
    {
        float delta = TvDelayMs - HomeTheatreDelayMs;
        if (MathF.Abs(delta) < 0.5f)
        {
            DelayDeltaText = "0 ms (Simultaneous Acoustic Arrival)";
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
    public async Task RefreshMicrophonesAsync()
    {
        try
        {
            MicrophoneStatusText = "Scanning recording endpoints...";
            var mics = await _calibrationService.GetAvailableMicrophonesAsync();

            _dispatcherService.Invoke(() =>
            {
                AvailableMicrophones.Clear();
                foreach (var m in mics)
                {
                    AvailableMicrophones.Add(m);
                }

                if (AvailableMicrophones.Count > 0)
                {
                    var savedId = _settingsService.CurrentSettings.SelectedCalibrationMicrophoneId;
                    SelectedMicrophone = AvailableMicrophones.FirstOrDefault(m => m.Id == savedId)
                                         ?? AvailableMicrophones.FirstOrDefault(m => m.IsDefaultRecording)
                                         ?? AvailableMicrophones.First();

                    IsMicrophoneAvailable = true;
                    IsMicrophoneAccessDenied = false;
                    MicrophoneStatusText = $"Selected: {SelectedMicrophone.Name}";
                }
                else
                {
                    SelectedMicrophone = null;
                    IsMicrophoneAvailable = false;
                    IsMicrophoneAccessDenied = false;
                    MicrophoneStatusText = "No physical microphone detected. Connect a microphone or headset.";
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh microphones");
            _dispatcherService.Invoke(() =>
            {
                MicrophoneStatusText = "Error scanning microphone devices.";
                IsMicrophoneAvailable = false;
            });
        }
    }

    private async Task VerifyMicrophoneAccessAsync(string deviceId)
    {
        bool accessible = await _calibrationService.CheckMicrophoneAccessAsync(deviceId);
        _dispatcherService.Invoke(() =>
        {
            if (accessible)
            {
                IsMicrophoneAccessDenied = false;
                MicrophoneStatusText = $"Microphone Ready: {SelectedMicrophone?.Name ?? "Active"}";
            }
            else
            {
                IsMicrophoneAccessDenied = true;
                MicrophoneStatusText = "Access Denied: Windows Privacy Settings block microphone capture.";
            }
        });
    }

    [RelayCommand]
    public async Task StartCalibrationAsync()
    {
        if (IsCalibrating) return;

        // Take snapshot of working delays prior to calibration
        _snapshotTvDelayMs = TvDelayMs;
        _snapshotHomeTheatreDelayMs = HomeTheatreDelayMs;
        _hasSnapshot = true;
        CanRestorePrevious = true;

        IsCalibrating = true;
        HasCalibrationResult = false;
        CanSaveCalibration = false;
        CalibrationProgress = 0f;
        CalibrationDetailedMessage = "Starting acoustic calibration procedure...";

        try
        {
            var result = await _calibrationService.RunCalibrationAsync(
                SelectedMicrophone?.Id,
                totalPasses: 3);

            _dispatcherService.Invoke(() =>
            {
                HasCalibrationResult = true;
                MeasuredTvArrivalMs = result.MeasuredTvArrivalMs;
                MeasuredHomeTheatreArrivalMs = result.MeasuredHomeTheatreArrivalMs;
                MeasuredDifferenceMs = result.ArrivalDifferenceMs;
                ConfidenceScore = result.ConfidenceScore;
                ConfidenceText = $"{result.ConfidenceScore * 100:F0}%";
                IsConfidenceAcceptable = result.IsConfidenceAcceptable;
                RecommendedTvDelayMs = result.RecommendedTvDelayMs;
                RecommendedHomeTheatreDelayMs = result.RecommendedHomeTheatreDelayMs;
                AcousticWarning = result.AcousticWarning;
                CalibrationDetailedMessage = result.Message;

                if (result.Success && result.IsConfidenceAcceptable)
                {
                    CanSaveCalibration = true;
                    CalibrationStatusText = "Calibration Successful. Review measured arrival difference and click 'Save Calibration' or 'Verify Synchronization'.";
                }
                else
                {
                    CanSaveCalibration = false;
                    CalibrationStatusText = "Calibration Unreliable. Measurements were discarded to preserve your settings.";
                    // Preserve working delays
                    RestoreWorkingDelays();
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Calibration failed abruptly");
            _dispatcherService.Invoke(() =>
            {
                CalibrationStatusText = $"Calibration error: {ex.Message}";
                RestoreWorkingDelays();
            });
        }
        finally
        {
            _dispatcherService.Invoke(() =>
            {
                IsCalibrating = false;
            });
        }
    }

    [RelayCommand]
    public void StopCalibration()
    {
        _calibrationService.Stop();
        RestoreWorkingDelays();
        CalibrationStatusText = "Calibration stopped. Working delays preserved.";
        IsCalibrating = false;
    }

    [RelayCommand]
    public async Task VerifySynchronizationAsync()
    {
        if (IsVerifying || IsCalibrating) return;

        IsVerifying = true;
        CalibrationDetailedMessage = "Playing synchronized verification chirp through both outputs...";

        try
        {
            float testTv = CanSaveCalibration ? RecommendedTvDelayMs : TvDelayMs;
            float testHt = CanSaveCalibration ? RecommendedHomeTheatreDelayMs : HomeTheatreDelayMs;

            await _calibrationService.PlayVerificationToneAsync(testTv, testHt);
            CalibrationDetailedMessage = "Verification chirp complete. Check if the sound arrived as a crisp, unified impulse.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to play verification chirp");
            CalibrationDetailedMessage = $"Verification playback error: {ex.Message}";
        }
        finally
        {
            IsVerifying = false;
        }
    }

    [RelayCommand]
    public async Task SaveCalibrationAsync()
    {
        if (!CanSaveCalibration) return;

        _dispatcherService.Invoke(() =>
        {
            TvDelayMs = RecommendedTvDelayMs;
            HomeTheatreDelayMs = RecommendedHomeTheatreDelayMs;
            _syncManager.SetDelay(AudioOutputBranch.TvHdmi, TvDelayMs);
            _syncManager.SetDelay(AudioOutputBranch.HomeTheatre35mm, HomeTheatreDelayMs);

            _settingsService.CurrentSettings.TvBranch.DelayMilliseconds = TvDelayMs;
            _settingsService.CurrentSettings.HomeTheatreBranch.DelayMilliseconds = HomeTheatreDelayMs;
            _settingsService.CurrentSettings.LastCalibrationTimeUtc = DateTime.UtcNow;
            _settingsService.CurrentSettings.LastCalibrationConfidence = ConfidenceScore;
            _settingsService.CurrentSettings.LastMeasuredDifferenceMs = MeasuredDifferenceMs;

            LastCalibrationTimeText = DateTime.Now.ToString("g");
            UpdateDelayDelta();

            CanSaveCalibration = false;
            CalibrationStatusText = "Calibration settings committed and saved successfully.";
            CalibrationDetailedMessage = $"Applied {TvDelayMs:F0} ms to TV and {HomeTheatreDelayMs:F0} ms to Home Theatre.";
        });

        await _settingsService.SaveSettingsAsync(_settingsService.CurrentSettings);
    }

    [RelayCommand]
    public void RestorePreviousSettings()
    {
        RestoreWorkingDelays();
        CanSaveCalibration = false;
        CalibrationStatusText = "Previous working delay settings restored.";
        CalibrationDetailedMessage = $"Delays reverted to TV: {TvDelayMs:F0} ms, Home Theatre: {HomeTheatreDelayMs:F0} ms.";
    }

    private void RestoreWorkingDelays()
    {
        if (!_hasSnapshot) return;

        TvDelayMs = _snapshotTvDelayMs;
        HomeTheatreDelayMs = _snapshotHomeTheatreDelayMs;
        _syncManager.SetDelay(AudioOutputBranch.TvHdmi, TvDelayMs);
        _syncManager.SetDelay(AudioOutputBranch.HomeTheatre35mm, HomeTheatreDelayMs);
        UpdateDelayDelta();
    }

    [RelayCommand]
    public void OpenMicrophonePrivacySettings()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ms-settings:privacy-microphone",
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to launch ms-settings:privacy-microphone");
        }
    }

    [RelayCommand]
    public void NavigateToManualSync()
    {
        _navigationService.NavigateTo<SynchronizationViewModel>();
    }

    private void OnCalibrationProgressChanged(object? sender, CalibrationProgress e)
    {
        _dispatcherService.Invoke(() =>
        {
            CalibrationProgress = e.ProgressPercentage;
            CalibrationDetailedMessage = e.StatusText;
        });
    }

    private void OnLiveSyncStatusChanged(object? sender, LiveSyncStatus status)
    {
        _dispatcherService.Invoke(() =>
        {
            UpdateLiveSyncStatus(status);
        });
    }

    private void UpdateLiveSyncStatus(LiveSyncStatus status)
    {
        LiveMonitoringStatusText = status.StatusDescription;
        EstimatedDriftText = $"{status.EstimatedDriftMs:+0.0;-0.0;0.0} ms";
        TotalCorrectionsText = $"{status.TotalCorrectionsApplied}";
        DriftLimitationNotice = status.LimitationNotice;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _calibrationService.ProgressChanged -= OnCalibrationProgressChanged;
            _liveSyncService.StatusChanged -= OnLiveSyncStatusChanged;
        }
        base.Dispose(disposing);
    }
}
