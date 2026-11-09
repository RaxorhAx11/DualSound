using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DualSound.Contracts.Models;
using DualSound.Core.Services;
using DualSound.Core.ViewModels;
using Microsoft.Extensions.Logging;

namespace DualSound.App.ViewModels;

/// <summary>
/// ViewModel for managing local JSON settings.
/// Strictly offline; never transmits data or requires network connectivity.
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly ILogger<SettingsViewModel> _logger;

    // Hardware Detection Settings
    [ObservableProperty]
    private bool _autoRefreshDevices;

    [ObservableProperty]
    private int _refreshIntervalSeconds;

    [ObservableProperty]
    private bool _showDisconnectedDevices;

    [ObservableProperty]
    private bool _showDisabledDevices;

    // Startup Behavior
    [ObservableProperty]
    private bool _autoStartEngine;

    [ObservableProperty]
    private bool _startMinimized;

    // Audio Engine & DSP Performance
    [ObservableProperty]
    private int _targetSampleRate = 48000;

    [ObservableProperty]
    private int _bufferLatencyMs = 50;

    [ObservableProperty]
    private bool _autoRestartOnDeviceLoss = true;

    // Logging & Diagnostics
    [ObservableProperty]
    private string _minimumLogLevel = "Information";

    [ObservableProperty]
    private string _settingsFilePath = string.Empty;

    [ObservableProperty]
    private string? _saveMessage;

    [ObservableProperty]
    private DateTime? _lastSavedUtc;

    public string[] AvailableLogLevels { get; } = ["Debug", "Information", "Warning", "Error"];

    public int[] AvailableSampleRates { get; } = [44100, 48000, 96000];

    public SettingsViewModel(ISettingsService settingsService, ILogger<SettingsViewModel> logger)
    {
        _settingsService = settingsService;
        _logger = logger;

        Title = "Settings";
        SettingsFilePath = _settingsService.SettingsFilePath;

        LoadCurrentSettings();
    }

    private void LoadCurrentSettings()
    {
        var settings = _settingsService.CurrentSettings;
        AutoRefreshDevices = settings.AutoRefreshDevices;
        RefreshIntervalSeconds = settings.RefreshIntervalSeconds;
        ShowDisconnectedDevices = settings.ShowDisconnectedDevices;
        ShowDisabledDevices = settings.ShowDisabledDevices;
        AutoStartEngine = settings.AutoStartEngine;
        StartMinimized = settings.StartMinimized;
        TargetSampleRate = settings.TargetSampleRate;
        BufferLatencyMs = settings.BufferLatencyMs;
        AutoRestartOnDeviceLoss = settings.AutoRestartOnDeviceLoss;
        MinimumLogLevel = settings.MinimumLogLevel;
        LastSavedUtc = settings.LastSavedAtUtc;
    }

    [RelayCommand]
    public async Task SaveSettingsAsync()
    {
        ClearError();
        SaveMessage = null;

        try
        {
            var updated = new AppSettings
            {
                Theme = "Dark",
                AutoRefreshDevices = AutoRefreshDevices,
                RefreshIntervalSeconds = Math.Clamp(RefreshIntervalSeconds, 1, 60),
                ShowDisconnectedDevices = ShowDisconnectedDevices,
                ShowDisabledDevices = ShowDisabledDevices,
                AutoStartEngine = AutoStartEngine,
                StartMinimized = StartMinimized,
                TargetSampleRate = TargetSampleRate,
                BufferLatencyMs = Math.Clamp(BufferLatencyMs, 10, 200),
                AutoRestartOnDeviceLoss = AutoRestartOnDeviceLoss,
                MinimumLogLevel = MinimumLogLevel,
                ActivePreset = _settingsService.CurrentSettings.ActivePreset,
                TvBranch = _settingsService.CurrentSettings.TvBranch,
                HomeTheatreBranch = _settingsService.CurrentSettings.HomeTheatreBranch,
                PreferredPrimaryDeviceId = _settingsService.CurrentSettings.PreferredPrimaryDeviceId,
                PreferredSecondaryDeviceId = _settingsService.CurrentSettings.PreferredSecondaryDeviceId,
                PreferredCaptureDeviceId = _settingsService.CurrentSettings.PreferredCaptureDeviceId,
                FavoriteDeviceIds = _settingsService.CurrentSettings.FavoriteDeviceIds,
                CustomPreset = _settingsService.CurrentSettings.CustomPreset
            };

            await _settingsService.SaveSettingsAsync(updated);
            LastSavedUtc = updated.LastSavedAtUtc;
            SaveMessage = "Preferences saved successfully to local JSON configuration.";
            _logger.LogInformation("Saved updated application preferences to disk");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings");
            SetError($"Error saving settings: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task ResetToDefaultsAsync()
    {
        ClearError();
        SaveMessage = null;

        try
        {
            var defaults = new AppSettings();
            await _settingsService.SaveSettingsAsync(defaults);
            LoadCurrentSettings();
            SaveMessage = "Settings restored to factory defaults.";
            _logger.LogInformation("Restored settings to factory defaults");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset settings");
            SetError($"Error resetting settings: {ex.Message}");
        }
    }

    [RelayCommand]
    public void OpenSettingsDirectory()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            SetError($"Could not open directory: {ex.Message}");
        }
    }
}
