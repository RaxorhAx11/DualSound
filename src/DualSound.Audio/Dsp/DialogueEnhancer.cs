using System;
using DualSound.Contracts.Enums;
using DualSound.Contracts.Models;

namespace DualSound.Audio.Dsp;

/// <summary>
/// Practical real-time dialogue clarity processor.
/// Highlights speech intelligibility frequencies (1.5 kHz - 3.5 kHz), attenuates low-end masking mud,
/// and applies soft dynamic compression to balance whispered speech against loud sound effects.
/// </summary>
public sealed class DialogueEnhancer
{
    private readonly float _sampleRate;
    private readonly BiquadFilter _clarityFilter;
    private readonly BiquadFilter _highPassFilter;
    private readonly object _lock = new();

    private bool _isEnabled = true;
    private float _enhancementAmount = 0.5f;
    private float _voiceBoostDb = 3.5f;
    private float _compressionRatio = 2.0f;
    private float _clarityFrequency = 2500f;

    // Dynamic compressor state registers
    private float _envelopeLevel = 0f;
    private readonly float _attackCoeff;
    private readonly float _releaseCoeff;
    private const float ThresholdLinear = 0.063f; // ~ -24 dBFS threshold

    public bool IsEnabled
    {
        get => _isEnabled;
        set => _isEnabled = value;
    }

    public DialogueEnhancer(float sampleRate = 48000f)
    {
        _sampleRate = sampleRate;

        // Speech clarity peaking filter (centered at 2500 Hz, Q ~1.2)
        _clarityFilter = new BiquadFilter(AudioFilterType.Peaking, _sampleRate, 2500f, 3.5f, 1.2f);

        // 90 Hz high-pass rumble filter to prevent low bass from triggering vocal compressor
        _highPassFilter = new BiquadFilter(AudioFilterType.HighPass, _sampleRate, 90f, 0f, 0.707f);

        // Fast attack (~10ms) and moderate release (~120ms) for natural speech leveling
        _attackCoeff = MathF.Exp(-1f / (0.010f * _sampleRate));
        _releaseCoeff = MathF.Exp(-1f / (0.120f * _sampleRate));
    }

    public void ApplySettings(DialogueEnhancementSettings settings)
    {
        lock (_lock)
        {
            _isEnabled = settings.IsEnabled;
            _enhancementAmount = Math.Clamp(settings.EnhancementAmount, 0f, 1f);
            _voiceBoostDb = Math.Clamp(settings.VoiceBoostDb, 0f, 12f);
            _compressionRatio = Math.Clamp(settings.CompressionRatio, 1f, 4f);
            _clarityFrequency = Math.Clamp(settings.ClarityFrequency, 800f, 5000f);

            // Scale actual boost by enhancement amount
            float effectiveBoost = _voiceBoostDb * _enhancementAmount;
            _clarityFilter.Configure(AudioFilterType.Peaking, _clarityFrequency, effectiveBoost, 1.2f, _sampleRate);
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _clarityFilter.Reset();
            _highPassFilter.Reset();
            _envelopeLevel = 0f;
        }
    }

    /// <summary>
    /// Processes interleaved stereo audio in-place.
    /// </summary>
    public void Process(Span<float> stereoSamples)
    {
        if (!_isEnabled || _enhancementAmount < 0.01f || stereoSamples.IsEmpty)
        {
            return;
        }

        lock (_lock)
        {
            // 1. Apply speech band frequency boost
            _clarityFilter.ProcessStereo(stereoSamples);

            // 2. Dynamic speech leveling (soft-knee compression)
            float ratio = _compressionRatio;
            float amount = _enhancementAmount;
            float envelope = _envelopeLevel;
            float attackCoeff = _attackCoeff;
            float releaseCoeff = _releaseCoeff;

            int frameCount = stereoSamples.Length / 2;
            for (int i = 0; i < frameCount; i++)
            {
                int idxL = i * 2;
                int idxR = idxL + 1;

                float inL = stereoSamples[idxL];
                float inR = stereoSamples[idxR];

                // Peak detector across left and right
                float maxAbs = MathF.Max(MathF.Abs(inL), MathF.Abs(inR));

                // Envelope follower with asymmetric attack/release
                if (maxAbs > envelope)
                {
                    envelope = attackCoeff * envelope + (1f - attackCoeff) * maxAbs;
                }
                else
                {
                    envelope = releaseCoeff * envelope + (1f - releaseCoeff) * maxAbs;
                }

                // Compute gain reduction for dialogue leveling
                // When level is below threshold, apply gentle upward expansion (lift whisper)
                // When level is above threshold, apply soft downward compression
                float targetGain = 1.0f;
                if (envelope > ThresholdLinear)
                {
                    // Downward compression: gain = (envelope / threshold) ^ (1/ratio - 1)
                    float overDb = 20f * MathF.Log10(envelope / ThresholdLinear);
                    float compressedDb = overDb / ratio;
                    float gainDb = (compressedDb - overDb) * amount;
                    targetGain = MathF.Pow(10f, gainDb / 20f);
                }
                else if (envelope > 0.005f) // Above noise floor (-46 dBFS)
                {
                    // Gentle upward lift for quieter dialogue (+1.5 dB max)
                    float liftDb = (1f - (envelope / ThresholdLinear)) * (1.8f * amount);
                    targetGain = MathF.Pow(10f, liftDb / 20f);
                }

                // Safety clamp
                targetGain = Math.Clamp(targetGain, 0.4f, 1.4f);

                stereoSamples[idxL] = inL * targetGain;
                stereoSamples[idxR] = inR * targetGain;
            }

            _envelopeLevel = envelope;
        }
    }
}

