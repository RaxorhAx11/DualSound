using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DualSound.Contracts.Models;
using DualSound.Core.Services;
using Microsoft.Extensions.Logging;

namespace DualSound.Infrastructure.Services;

/// <summary>
/// Persists application settings to local JSON storage in %APPDATA%\DualSound\settings.json.
/// Operates completely offline without cloud dependencies or buffer storage.
/// </summary>
public sealed class JsonSettingsService : ISettingsService
{
    private readonly ILogger<JsonSettingsService> _logger;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions;
    private AppSettings _currentSettings;

    public AppSettings CurrentSettings => _currentSettings;
    public string SettingsFilePath { get; }
    public event EventHandler<AppSettings>? SettingsChanged;

    public JsonSettingsService(ILogger<JsonSettingsService> logger, string? customSettingsPath = null)
    {
        _logger = logger;
        _currentSettings = new AppSettings();

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        if (!string.IsNullOrWhiteSpace(customSettingsPath))
        {
            SettingsFilePath = customSettingsPath;
        }
        else
        {
            var appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var settingsDirectory = Path.Combine(appDataFolder, "DualSound");

            try
            {
                if (!Directory.Exists(settingsDirectory))
                {
                    Directory.CreateDirectory(settingsDirectory);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create settings directory {Directory}", settingsDirectory);
            }

            SettingsFilePath = Path.Combine(settingsDirectory, "settings.json");
        }
    }

    /// <inheritdoc />
    public async Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                _logger.LogInformation("No settings file found at {Path}. Creating default settings", SettingsFilePath);
                _currentSettings = new AppSettings();
                await SaveSettingsInternalAsync(_currentSettings, cancellationToken);
                return _currentSettings;
            }

            var json = await File.ReadAllTextAsync(SettingsFilePath, cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
            {
                _logger.LogWarning("Settings file at {Path} is empty. Using defaults", SettingsFilePath);
                _currentSettings = new AppSettings();
                return _currentSettings;
            }

            var loaded = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
            _currentSettings = loaded ?? new AppSettings();
            _logger.LogInformation("Successfully loaded settings from {Path}", SettingsFilePath);
            return _currentSettings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading settings file at {Path}. Falling back to default settings", SettingsFilePath);
            _currentSettings = new AppSettings();
            return _currentSettings;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _fileLock.WaitAsync(cancellationToken);
        try
        {
            await SaveSettingsInternalAsync(settings, cancellationToken);
            _currentSettings = settings;
        }
        finally
        {
            _fileLock.Release();
        }

        try
        {
            SettingsChanged?.Invoke(this, _currentSettings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error notifying SettingsChanged subscribers");
        }
    }

    private async Task SaveSettingsInternalAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        settings.LastSavedAtUtc = DateTime.UtcNow;

        var directory = Path.GetDirectoryName(SettingsFilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        var tempFilePath = SettingsFilePath + ".tmp";

        // Write to temporary file first for atomic overwrite
        await File.WriteAllTextAsync(tempFilePath, json, cancellationToken);
        File.Move(tempFilePath, SettingsFilePath, overwrite: true);

        _logger.LogInformation("Saved application settings to {Path}", SettingsFilePath);
    }
}
