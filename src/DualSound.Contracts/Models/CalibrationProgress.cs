using System;

namespace DualSound.Contracts.Models;

public enum CalibrationStage
{
    NotStarted,
    PositioningNotice,
    CheckingMicrophone,
    MeasuringTv,
    MeasuringHomeTheatre,
    AnalyzingCorrelation,
    Verifying,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// Real-time progress reporting during multi-step acoustic sync calibration.
/// </summary>
public sealed record CalibrationProgress(
    CalibrationStage Stage,
    int CurrentPass,
    int TotalPasses,
    float ProgressPercentage,
    string StatusText,
    float CurrentSignalLevelDb = -60f);

