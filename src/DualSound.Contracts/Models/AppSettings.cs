using System;
using System.Collections.Generic;
using DualSound.Contracts.Enums;

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
    /// Preferred capture / recording device ID (e.g. VB-Audio CABLE Output or Microphone).
    /// </summary>
    public string? PreferredCaptureDeviceId { get; set; }

    /// <summary>
    /// Currently active sound profile preset.
    /// </summary>
    public AudioPresetType ActivePreset { get; set; } = AudioPresetType.MovieTheatre;

    /// <summary>
    /// Independent branch settings for TV (HDMI / DisplayPort).
    /// </summary>
    public OutputBranchSettings TvBranch { get; set; } = new(AudioOutputBranch.TvHdmi);

    /// <summary>
    /// Independent branch settings for Home Theatre (3.5 mm analog jack).
    /// </summary>
    public OutputBranchSettings HomeTheatreBranch { get; set; } = new(AudioOutputBranch.HomeTheatre35mm);

    /// <summary>
    /// Custom preset configuration (saved when user customizes EQ/DSP).
    /// </summary>
    public AudioPreset? CustomPreset { get; set; }

    /// <summary>
    /// Whether to automatically start the audio engine when the application launches.
    /// </summary>
    public bool AutoStartEngine { get; set; } = false;

    /// <summary>
    /// Whether to minimize to taskbar on startup.
    /// </summary>
    public bool StartMinimized { get; set; } = false;

    /// <summary>
    /// Internal target processing sample rate in Hertz. Default 48000 Hz.
    /// </summary>
    public int TargetSampleRate { get; set; } = 48000;

    /// <summary>
    /// Output buffer latency target in milliseconds. Default 50 ms.
    /// </summary>
    public int BufferLatencyMs { get; set; } = 50;

    /// <summary>
    /// Automatically restart audio engine when a device disconnects/reconnects.
    /// </summary>
    public bool AutoRestartOnDeviceLoss { get; set; } = true;

    /// <summary>
    /// User favorite device IDs for quick routing access.
    /// </summary>
    public List<string> FavoriteDeviceIds { get; set; } = [];

    /// <summary>
    /// Timestamp when settings were last saved to disk.
    /// </summary>
    public DateTime? LastSavedAtUtc { get; set; }

    /// <summary>
    /// Whether the initial multi-step setup wizard has been successfully completed.
    /// </summary>
    public bool IsSetupWizardCompleted { get; set; } = false;

    /// <summary>
    /// Detected or user-selected virtual audio playback endpoint ID (e.g. CABLE Input).
    /// </summary>
    public string? VirtualPlaybackDeviceId { get; set; }

    /// <summary>
    /// Detected or user-selected virtual audio recording endpoint ID (e.g. CABLE Output).
    /// </summary>
    public string? VirtualRecordingDeviceId { get; set; }

    /// <summary>
    /// Whether to restore the previous Windows default playback device on application exit.
    /// Default is false to strictly prevent unexpected audio routing changes unless explicitly enabled.
    /// </summary>
    public bool RestoreDefaultDeviceOnExit { get; set; } = false;

    /// <summary>
    /// Stored previous Windows default playback device ID for optional user-requested restoration.
    /// </summary>
    public string? PreviousDefaultPlaybackDeviceId { get; set; }

    /// <summary>
    /// Whether to show the routing mismatch warning banner when desktop audio is not routed via the virtual cable.
    /// </summary>
    public bool ShowRoutingWarningBanner { get; set; } = true;

    /// <summary>
    /// Minimum logging level persisted.
    /// </summary>
    public string MinimumLogLevel { get; set; } = "Information";
}
