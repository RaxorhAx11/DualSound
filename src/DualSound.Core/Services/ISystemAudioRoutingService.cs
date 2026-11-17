using System;
using System.Threading;
using System.Threading.Tasks;
using DualSound.Contracts.Enums;
using DualSound.Contracts.Models;

namespace DualSound.Core.Services;

/// <summary>
/// Service managing system-wide Windows desktop audio routing verification,
/// virtual audio cable detection and pairing, bypass diagnostics, safe test tone generation,
/// and Windows Sound Settings interaction.
/// </summary>
public interface ISystemAudioRoutingService : IDisposable
{
    /// <summary>
    /// Current real-time diagnostic status of system-wide audio routing.
    /// </summary>
    SystemAudioRoutingStatus CurrentStatus { get; }

    /// <summary>
    /// Paired virtual audio cable endpoints (e.g. CABLE Input + CABLE Output) if installed.
    /// </summary>
    VirtualAudioCablePair? DetectedVirtualCable { get; }

    /// <summary>
    /// Whether an audio test tone is currently playing.
    /// </summary>
    bool IsPlayingTestTone { get; }

    /// <summary>
    /// Fires when the routing diagnostic status changes.
    /// </summary>
    event EventHandler<SystemAudioRoutingStatus>? RoutingStatusChanged;

    /// <summary>
    /// Runs a comprehensive 7-point diagnostic of Windows system audio routing,
    /// endpoint accessibility, and potential bypasses.
    /// </summary>
    Task<SystemAudioRoutingStatus> ValidateRoutingAsync();

    /// <summary>
    /// Discovers and pairs Windows virtual audio cable endpoints.
    /// </summary>
    Task<VirtualAudioCablePair?> DetectVirtualAudioCableAsync();

    /// <summary>
    /// Launches the Windows Sound Settings applet (ms-settings:sound) via shell execute.
    /// </summary>
    void OpenWindowsSoundSettings();

    /// <summary>
    /// Plays a safe test tone (-12 dBFS sine wave with soft windowing) to a specific output branch or both.
    /// </summary>
    Task<bool> PlayTestToneAsync(AudioOutputBranch? targetBranch = null, float durationSeconds = 3.0f, float volume = 0.25f, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops any currently active test tone immediately.
    /// </summary>
    void StopTestTone();
}

