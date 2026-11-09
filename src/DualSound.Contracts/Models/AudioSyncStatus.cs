using DualSound.Contracts.Enums;

namespace DualSound.Contracts.Models;

/// <summary>
/// Real-time synchronization metrics and health telemetry for an audio output branch.
/// </summary>
public readonly record struct AudioSyncStatus(
    AudioOutputBranch Branch,
    float ConfiguredDelayMs,
    float EstimatedLatencyMs,
    float BufferOccupancyPercentage,
    long DriftSamples,
    long UnderrunCount,
    long OverrunCount,
    bool IsSynchronized,
    string HealthDescription)
{
    public static AudioSyncStatus CreateDefault(AudioOutputBranch branch) => new(
        Branch: branch,
        ConfiguredDelayMs: 0f,
        EstimatedLatencyMs: 0f,
        BufferOccupancyPercentage: 0f,
        DriftSamples: 0,
        UnderrunCount: 0,
        OverrunCount: 0,
        IsSynchronized: true,
        HealthDescription: "Synchronized");
}

