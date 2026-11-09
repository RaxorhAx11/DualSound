using System;
using DualSound.Contracts.Enums;
using DualSound.Contracts.Models;

namespace DualSound.Core.Services;

/// <summary>
/// Manages synchronization, millisecond delay lines, clock drift, and buffer health
/// between independent audio output branches (TV HDMI and Home Theatre 3.5mm).
/// </summary>
public interface IAudioSyncManager : IDisposable
{
    /// <summary>
    /// Configures delay in milliseconds for a specific branch (0 ms to 500 ms).
    /// </summary>
    void SetDelay(AudioOutputBranch branch, float delayMs);

    /// <summary>
    /// Gets current configured delay in milliseconds for a branch.
    /// </summary>
    float GetDelay(AudioOutputBranch branch);

    /// <summary>
    /// Gets real-time sync metrics and health telemetry for a branch.
    /// </summary>
    AudioSyncStatus GetStatus(AudioOutputBranch branch);

    /// <summary>
    /// Resets all delay buffers, clearing accumulated latency, drift, and underrun counters.
    /// </summary>
    void Reset();

    /// <summary>
    /// Raised periodically when synchronization telemetry updates.
    /// </summary>
    event EventHandler<AudioSyncStatus>? StatusUpdated;
}

