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
/// Strictly offline; does not persist temporary audio buffers or communicate with any network service.
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly ILogger<SettingsViewModel> _logger;

    [ObservableProperty]
    private bool _autoRefreshDevices;

    [ObservableProperty]
    private int _refreshIntervalSeconds;

    [ObservableProperty]
    private bool _showDisconnectedDevices;

    [ObservableProperty]
    private bool _showDisabledDevices;

    [ObservableProperty]
    private string _settingsFilePath = string.Empty;

    [ObservableProperty]
    private string? _saveMessage;

    [ObservableProperty]
    private DateTime? _lastSavedUtc;

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
                FavoriteDeviceIds = _settingsService.CurrentSettings.FavoriteDeviceIds,
                PreferredPrimaryDeviceId = _settingsService.CurrentSettings.PreferredPrimaryDeviceId,
                PreferredSecondaryDeviceId = _settingsService.CurrentSettings.PreferredSecondaryDeviceId,
                MinimumLogLevel = _settingsService.CurrentSettings.MinimumLogLevel
            };

            await _settingsService.SaveSettingsAsync(updated);
            LastSavedUtc = updated.LastSavedAtUtc;
            SaveMessage = "Preferences saved successfully to local disk.";
            _logger.LogInformation("Saved updated settings via SettingsViewModel");
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

        var defaults = new AppSettings();
        AutoRefreshDevices = defaults.AutoRefreshDevices;
        RefreshIntervalSeconds = defaults.RefreshIntervalSeconds;
        ShowDisconnectedDevices = defaults.ShowDisconnectedDevices;
        ShowDisabledDevices = defaults.ShowDisabledDevices;

        await SaveSettingsAsync();
        SaveMessage = "Settings reset to default values.";
    }

    [RelayCommand]
    private void OpenSettingsFolder()
    {
        try
        {
            var folder = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not open settings folder");
        }
    }
}

