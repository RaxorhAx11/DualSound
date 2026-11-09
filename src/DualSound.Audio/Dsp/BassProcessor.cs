using System;
using DualSound.Contracts.Enums;
using DualSound.Contracts.Models;

namespace DualSound.Audio.Dsp;

/// <summary>
/// Low-frequency bass enhancement and crossover processor.
/// Specifically optimized for stereo analog outputs feeding home theatre setups
/// with a dedicated subwoofer and satellite speakers.
/// Supports low-pass crossover filtering, low-end parametric gain boost, and monaural bass summing.
/// </summary>
public sealed class BassProcessor
{
    private readonly float _sampleRate;
    private readonly BiquadFilter _bassBoostFilter;
    private readonly BiquadFilter _crossoverLowPassFilter;
    private readonly object _lock = new();

    private bool _isEnabled = true;
    private float _bassGainDb = 4.0f;
    private float _crossoverFrequency = 120f;
    private bool _lowPassEnabled = true;
    private bool _monoSubwooferMix = true;

    public bool IsEnabled
    {
        get => _isEnabled;
        set => _isEnabled = value;
    }

    public BassProcessor(float sampleRate = 48000f)
    {
        _sampleRate = sampleRate;

        // 80 Hz low-shelf / peaking bass boost filter
        _bassBoostFilter = new BiquadFilter(AudioFilterType.LowShelf, _sampleRate, 80f, 4.0f, 0.9f);

        // 2nd-order Butterworth low-pass crossover filter (Q = 0.707)
        _crossoverLowPassFilter = new BiquadFilter(AudioFilterType.LowPass, _sampleRate, 120f, 0f, 0.707f);
    }

    public void ApplySettings(BassProcessingSettings settings)
    {
        lock (_lock)
        {
            _isEnabled = settings.IsEnabled;
            _bassGainDb = Math.Clamp(settings.BassGainDb, 0f, 15f);
            _crossoverFrequency = Math.Clamp(settings.CrossoverFrequency, 40f, 300f);
            _lowPassEnabled = settings.LowPassFilterEnabled;
            _monoSubwooferMix = settings.MonoSubwooferMix;

            _bassBoostFilter.Configure(AudioFilterType.LowShelf, Math.Min(_crossoverFrequency, 100f), _bassGainDb, 0.9f, _sampleRate);
            _crossoverLowPassFilter.Configure(AudioFilterType.LowPass, _crossoverFrequency, 0f, 0.707f, _sampleRate);
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _bassBoostFilter.Reset();
            _crossoverLowPassFilter.Reset();
        }
    }

    /// <summary>
    /// Processes interleaved stereo audio in-place.
    /// </summary>
    public void Process(Span<float> stereoSamples)
    {
        if (!_isEnabled || stereoSamples.IsEmpty)
        {
            return;
        }

        lock (_lock)
        {
            // 1. Monaural bass summing if enabled:
            // Combines left and right so single subwoofer / asymmetrical speaker setups get full energy
            if (_monoSubwooferMix)
            {
                int frameCount = stereoSamples.Length / 2;
                for (int i = 0; i < frameCount; i++)
                {
                    int idxL = i * 2;
                    int idxR = idxL + 1;

                    float inL = stereoSamples[idxL];
                    float inR = stereoSamples[idxR];

                    // Pure mono downmix: ensures single subwoofer and single working satellite speaker
                    // receive full audio from both stereo channels without dropping panned audio
                    float mono = (inL + inR) * 0.5f;
                    stereoSamples[idxL] = mono;
                    stereoSamples[idxR] = mono;
                }
            }

            // 2. Low-frequency shelf / parametric bass boost
            if (_bassGainDb > 0.05f)
            {
                _bassBoostFilter.ProcessStereo(stereoSamples);
            }

            // 3. Optional low-pass crossover filtering (if feeding a dedicated sub branch)
            if (_lowPassEnabled)
            {
                _crossoverLowPassFilter.ProcessStereo(stereoSamples);
            }
        }
    }
}
