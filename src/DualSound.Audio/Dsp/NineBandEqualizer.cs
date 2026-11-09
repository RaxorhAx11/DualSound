using System;
using DualSound.Contracts.Enums;
using DualSound.Contracts.Models;

namespace DualSound.Audio.Dsp;

/// <summary>
/// 9-band digital biquad graphic equalizer.
/// Standard center frequencies: 60 Hz, 120 Hz, 250 Hz, 500 Hz, 1 kHz, 2 kHz, 4 kHz, 8 kHz, 16 kHz.
/// Processes interleaved stereo audio in-place with zero heap allocations.
/// </summary>
public sealed class NineBandEqualizer
{
    public static readonly float[] StandardFrequencies = [60f, 120f, 250f, 500f, 1000f, 2000f, 4000f, 8000f, 16000f];

    private readonly BiquadFilter[] _filters;
    private readonly float _sampleRate;
    private bool _isEnabled = true;
    private readonly object _lock = new();

    public bool IsEnabled
    {
        get => _isEnabled;
        set => _isEnabled = value;
    }

    public NineBandEqualizer(float sampleRate = 48000f)
    {
        _sampleRate = sampleRate;
        _filters = new BiquadFilter[StandardFrequencies.Length];

        for (int i = 0; i < StandardFrequencies.Length; i++)
        {
            // Q factor 1.414 gives ~1 octave bandwidth per band
            _filters[i] = new BiquadFilter(AudioFilterType.Peaking, _sampleRate, StandardFrequencies[i], 0f, 1.414f);
        }
    }

    /// <summary>
    /// Applies an entire EqualizerSettings snapshot safely.
    /// </summary>
    public void ApplySettings(EqualizerSettings settings)
    {
        lock (_lock)
        {
            _isEnabled = settings.IsEnabled;
            for (int i = 0; i < _filters.Length && i < settings.Bands.Count; i++)
            {
                var band = settings.Bands[i];
                _filters[i].Configure(AudioFilterType.Peaking, band.Frequency, band.GainDb, band.Q, _sampleRate);
            }
        }
    }

    /// <summary>
    /// Updates gain for a specific band index (0 to 8).
    /// </summary>
    public void SetBandGain(int bandIndex, float gainDb)
    {
        if (bandIndex < 0 || bandIndex >= _filters.Length) return;

        lock (_lock)
        {
            var filter = _filters[bandIndex];
            filter.Configure(AudioFilterType.Peaking, StandardFrequencies[bandIndex], gainDb, 1.414f, _sampleRate);
        }
    }

    /// <summary>
    /// Resets all bands to 0 dB flat response and clears filter history.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            for (int i = 0; i < _filters.Length; i++)
            {
                _filters[i].Configure(AudioFilterType.Peaking, StandardFrequencies[i], 0f, 1.414f, _sampleRate);
                _filters[i].Reset();
            }
        }
    }

    /// <summary>
    /// Processes interleaved stereo float samples in-place.
    /// </summary>
    public void Process(Span<float> stereoSamples)
    {
        if (!_isEnabled || stereoSamples.IsEmpty)
        {
            return;
        }

        lock (_lock)
        {
            for (int i = 0; i < _filters.Length; i++)
            {
                _filters[i].ProcessStereo(stereoSamples);
            }
        }
    }
}

