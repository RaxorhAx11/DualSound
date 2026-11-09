namespace DualSound.Contracts.Models;

/// <summary>
/// Represents a single parametric frequency band in an audio equalizer.
/// </summary>
public sealed class EqualizerBand
{
    /// <summary>
    /// Center frequency in Hertz.
    /// </summary>
    public float Frequency { get; set; }

    /// <summary>
    /// Gain boost or cut in decibels (-12 dB to +12 dB).
    /// </summary>
    public float GainDb { get; set; }

    /// <summary>
    /// Quality factor Q controlling bandwidth. Default 1.414 (~1 octave).
    /// </summary>
    public float Q { get; set; } = 1.414f;

    public EqualizerBand() { }

    public EqualizerBand(float frequency, float gainDb = 0f, float q = 1.414f)
    {
        Frequency = frequency;
        GainDb = gainDb;
        Q = q;
    }
}

