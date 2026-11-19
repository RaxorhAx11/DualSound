using System;
using System.Collections.Generic;

namespace DualSound.Contracts.Models;

/// <summary>
/// Result of an individual measurement pass during acoustic sync calibration.
/// </summary>
public sealed record CalibrationPassMeasurement(
    int PassNumber,
    float TvArrivalMs,
    float HomeTheatreArrivalMs,
    float DifferenceMs,
    float TvPeakToNoiseRatio,
    float HomeTheatrePeakToNoiseRatio,
    bool IsAccepted,
    string? RejectionReason = null);

/// <summary>
/// Overall acoustic calibration outcome containing acoustic arrival measurements,
/// signal correlation confidence, recommended delay compensation, and health diagnostics.
/// </summary>
public sealed class AcousticCalibrationResult
{
    /// <summary>
    /// Whether calibration completed successfully with acceptable correlation confidence.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Measured acoustic sound arrival time for the TV output (in milliseconds relative to playback start).
    /// </summary>
    public float MeasuredTvArrivalMs { get; set; }

    /// <summary>
    /// Measured acoustic sound arrival time for the Home Theatre output (in milliseconds relative to playback start).
    /// </summary>
    public float MeasuredHomeTheatreArrivalMs { get; set; }

    /// <summary>
    /// Acoustic arrival difference in milliseconds: (TvArrival - HomeTheatreArrival).
    /// Positive means TV sound arrives later than Home Theatre; negative means Home Theatre arrives later.
    /// </summary>
    public float ArrivalDifferenceMs { get; set; }

    /// <summary>
    /// Overall confidence score from 0.0f (unusable / noise) to 1.0f (studio precision).
    /// </summary>
    public float ConfidenceScore { get; set; }

    /// <summary>
    /// Whether the confidence score exceeds the minimum required reliability threshold (typically 0.65).
    /// </summary>
    public bool IsConfidenceAcceptable { get; set; }

    /// <summary>
    /// Ambient noise floor in dBFS measured by the microphone prior to test chirp.
    /// </summary>
    public float AmbientNoiseFloorDb { get; set; }

    /// <summary>
    /// Calculated recommended delay in milliseconds for the TV output branch (0 to 500 ms).
    /// </summary>
    public float RecommendedTvDelayMs { get; set; }

    /// <summary>
    /// Calculated recommended delay in milliseconds for the Home Theatre output branch (0 to 500 ms).
    /// </summary>
    public float RecommendedHomeTheatreDelayMs { get; set; }

    /// <summary>
    /// Detailed diagnostic or explanation message presented to the user.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Warning or explanation about room reflections, acoustics, or microphone positioning.
    /// </summary>
    public string? AcousticWarning { get; set; }

    /// <summary>
    /// Breakdown of individual measurement passes used for robust averaging.
    /// </summary>
    public List<CalibrationPassMeasurement> Passes { get; set; } = [];

    /// <summary>
    /// Timestamp when calibration completed.
    /// </summary>
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    public static AcousticCalibrationResult Failed(string message, string? acousticWarning = null, float confidence = 0f) => new()
    {
        Success = false,
        IsConfidenceAcceptable = false,
        ConfidenceScore = confidence,
        Message = message,
        AcousticWarning = acousticWarning ?? "Acoustic calibration could not be completed reliably. Previous working delay settings have been preserved."
    };
}

