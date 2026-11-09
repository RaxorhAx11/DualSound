using System;
using System.Threading;
using System.Threading.Tasks;
using DualSound.Contracts.Models;

namespace DualSound.Core.Services;

/// <summary>
/// Service abstraction for loading and persisting user application settings locally in JSON.
/// Fully offline; never uses external cloud services.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Current cached in-memory settings.
    /// </summary>
    AppSettings CurrentSettings { get; }

    /// <summary>
    /// Path to the local JSON configuration file on disk.
    /// </summary>
    string SettingsFilePath { get; }

    /// <summary>
    /// Raised whenever configuration settings have been updated or reloaded.
    /// </summary>
    event EventHandler<AppSettings>? SettingsChanged;

    /// <summary>
    /// Loads settings from the local JSON file, creating defaults if not yet created.
    /// </summary>
    Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists given settings to the local JSON file.
    /// </summary>
    Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default);
}

