using System.Collections.Generic;

namespace DualSound.Contracts.Models;

/// <summary>
/// Configuration for a 9-band digital biquad graphic equalizer.
/// Frequency centers: 60 Hz, 120 Hz, 250 Hz, 500 Hz, 1 kHz, 2 kHz, 4 kHz, 8 kHz, 16 kHz.
/// </summary>
public sealed class EqualizerSettings
{
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// The 9 discrete equalizer bands.
    /// </summary>
    public List<EqualizerBand> Bands { get; set; } = [];

    public EqualizerSettings()
    {
        InitializeStandardBands();
    }

    public void InitializeStandardBands()
    {
        Bands =
        [
            new EqualizerBand(60f),
            new EqualizerBand(120f),
            new EqualizerBand(250f),
            new EqualizerBand(500f),
            new EqualizerBand(1000f),
            new EqualizerBand(2000f),
            new EqualizerBand(4000f),
            new EqualizerBand(8000f),
            new EqualizerBand(16000f)
        ];
    }

    public EqualizerSettings Clone()
    {
        var clone = new EqualizerSettings
        {
            IsEnabled = IsEnabled,
            Bands = new List<EqualizerBand>(Bands.Count)
        };
        foreach (var b in Bands)
        {
            clone.Bands.Add(new EqualizerBand(b.Frequency, b.GainDb, b.Q));
        }
        return clone;
    }
}

