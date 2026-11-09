namespace DualSound.Contracts.Models;

/// <summary>
/// Audio safety and dynamic protection parameters.
/// Guards against DAC clipping, digital distortion, volume blasts, and invalid floating-point numbers.
/// </summary>
public sealed class AudioSafetySettings
{
    /// <summary>
    /// Master lookahead / instant peak limiter enable toggle.
    /// </summary>
    public bool PeakLimiterEnabled { get; set; } = true;

    /// <summary>
    /// Limiting threshold in decibels relative to full scale. Default -0.5 dBFS.
    /// </summary>
    public float ThresholdDbfs { get; set; } = -0.5f;

    /// <summary>
    /// Release time constant in milliseconds for limiter gain recovery. Default 50 ms.
    /// </summary>
    public float ReleaseMs { get; set; } = 50f;

    /// <summary>
    /// Hard clip ceiling absolute value (0.944f = -0.5 dBFS).
    /// </summary>
    public float CeilingLinear => 0.94406f;

    public AudioSafetySettings Clone() => new()
    {
        PeakLimiterEnabled = PeakLimiterEnabled,
        ThresholdDbfs = ThresholdDbfs,
        ReleaseMs = ReleaseMs
    };
}

