using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DualSound.Contracts.Models;

namespace DualSound.Core.Services;

/// <summary>
/// Service managing acoustic synchronization calibration using physical microphone capture
/// and audio signal cross-correlation between TV and Home Theatre outputs.
/// </summary>
public interface IAcousticCalibrationService : IDisposable
{
    /// <summary>
    /// Gets whether a calibration run is currently in progress.
    /// </summary>
    bool IsCalibrating { get; }

    /// <summary>
    /// Gets whether verification tone playback is active.
    /// </summary>
    bool IsVerifying { get; }

    /// <summary>
    /// Raised when calibration progress or status changes.
    /// </summary>
    event EventHandler<CalibrationProgress>? ProgressChanged;

    /// <summary>
    /// Enumerates active physical microphone and recording endpoints suitable for acoustic measurement.
    /// </summary>
    Task<IReadOnlyList<AudioDeviceInfo>> GetAvailableMicrophonesAsync();

    /// <summary>
    /// Tests whether the specified microphone is available and accessible.
    /// </summary>
    Task<bool> CheckMicrophoneAccessAsync(string? microphoneDeviceId);

    /// <summary>
    /// Executes the full acoustic calibration procedure:
    /// plays calibration chirps through TV and Home Theatre independently,
    /// records arrival signals via the selected microphone,
    /// and calculates differential arrival delay using cross-correlation.
    /// </summary>
    Task<AcousticCalibrationResult> RunCalibrationAsync(
        string? microphoneDeviceId,
        int totalPasses = 3,
        IProgress<CalibrationProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Plays simultaneous verification chirps through both outputs with specified delays applied.
    /// </summary>
    Task PlayVerificationToneAsync(
        float tvDelayMs,
        float homeTheatreDelayMs,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels any active calibration or verification playback.
    /// </summary>
    void Stop();
}

