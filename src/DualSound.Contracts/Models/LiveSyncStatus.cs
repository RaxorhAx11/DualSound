using System;

namespace DualSound.Contracts.Models;

/// <summary>
/// Status and metrics for continuous live synchronization monitoring.
/// </summary>
public sealed record LiveSyncStatus(
    bool IsEnabled,
    bool IsActive,
    float EstimatedDriftMs,
    long DriftSamples,
    long TotalCorrectionsApplied,
    string StatusDescription,
    string? LimitationNotice = null,
    bool IsReliable = true)
{
    public static LiveSyncStatus Disabled => new(
        IsEnabled: false,
        IsActive: false,
        EstimatedDriftMs: 0f,
        DriftSamples: 0,
        TotalCorrectionsApplied: 0,
        StatusDescription: "Disabled",
        LimitationNotice: null,
        IsReliable: true);

    public static LiveSyncStatus EngineStopped => new(
        IsEnabled: true,
        IsActive: false,
        EstimatedDriftMs: 0f,
        DriftSamples: 0,
        TotalCorrectionsApplied: 0,
        StatusDescription: "Monitoring Inactive (Audio Engine Stopped)",
        LimitationNotice: "Live drift monitoring requires active audio playback to measure hardware DAC clock differences.",
        IsReliable: false);
}

