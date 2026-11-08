using System;
using System.Collections.Generic;

namespace DualSound.Contracts.Models;

/// <summary>
/// User configuration settings persisted locally in JSON format.
/// Fully offline; never transmits data or requires network connectivity.
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// UI theme setting. Default is "Dark".
    /// </summary>
    public string Theme { get; set; } = "Dark";

    /// <summary>
    /// Enables automatic real-time detection of Windows device additions/removals/state changes.
    /// </summary>
    public bool AutoRefreshDevices { get; set; } = true;

    /// <summary>
    /// Fallback polling refresh interval in seconds when auto-refresh is active.
    /// </summary>
    public int RefreshIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// Whether to display unplugged or disconnected audio devices in the device list.
    /// </summary>
    public bool ShowDisconnectedDevices { get; set; } = true;

    /// <summary>
    /// Whether to display disabled audio endpoints in the device list.
    /// </summary>
    public bool ShowDisabledDevices { get; set; } = true;

    /// <summary>
    /// Preferred primary output device ID (e.g. TV via HDMI).
    /// </summary>
    public string? PreferredPrimaryDeviceId { get; set; }

    /// <summary>
    /// Preferred secondary output device ID (e.g. Home Theatre via 3.5mm jack).
    /// </summary>
    public string? PreferredSecondaryDeviceId { get; set; }

    /// <summary>
    /// User favorite device IDs for quick routing access.
    /// </summary>
    public List<string> FavoriteDeviceIds { get; set; } = [];

    /// <summary>
    /// Timestamp when settings were last saved to disk.
    /// </summary>
    public DateTime? LastSavedAtUtc { get; set; }

    /// <summary>
    /// Minimum logging level persisted.
    /// </summary>
    public string MinimumLogLevel { get; set; } = "Information";
}
