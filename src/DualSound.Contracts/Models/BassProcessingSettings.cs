namespace DualSound.Contracts.Models;

/// <summary>
/// Settings for real-time low-frequency bass enhancement and crossover filtering.
/// Tailored for home theatre subwoofers and stereo analog jack configurations.
/// </summary>
public sealed class BassProcessingSettings
{
    /// <summary>
    /// Whether bass processing is enabled on this branch.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Bass gain boost in decibels (0 to +12 dB). Default is +4.0 dB.
    /// </summary>
    public float BassGainDb { get; set; } = 4.0f;

    /// <summary>
    /// Low-pass crossover cutoff frequency in Hertz (60 Hz to 200 Hz). Default is 120 Hz.
    /// </summary>
    public float CrossoverFrequency { get; set; } = 120f;

    /// <summary>
    /// Whether the low-pass filter slope is active to isolate sub-bass frequencies.
    /// </summary>
    public bool LowPassFilterEnabled { get; set; } = true;

    /// <summary>
    /// Sums left and right bass components to mono (L+R) to drive mono subwoofer configurations.
    /// Crucial for hardware setups where the home theatre has a dedicated sub and single speaker.
    /// </summary>
    public bool MonoSubwooferMix { get; set; } = true;

    public BassProcessingSettings Clone() => new()
    {
        IsEnabled = IsEnabled,
        BassGainDb = BassGainDb,
        CrossoverFrequency = CrossoverFrequency,
        LowPassFilterEnabled = LowPassFilterEnabled,
        MonoSubwooferMix = MonoSubwooferMix
    };
}

