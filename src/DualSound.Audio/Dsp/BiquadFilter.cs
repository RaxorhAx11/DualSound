using System;
using DualSound.Contracts.Enums;

namespace DualSound.Audio.Dsp;

/// <summary>
/// High-performance digital biquad filter implementing Robert Bristow-Johnson (RBJ) Audio EQ formulas.
/// Direct Form I Transposed realization for maximum numerical stability with 32-bit floating-point audio.
/// Processes interleaved stereo audio in-place with zero memory allocations.
/// </summary>
public sealed class BiquadFilter
{
    private AudioFilterType _type;
    private float _sampleRate;
    private float _frequency;
    private float _gainDb;
    private float _q;

    // Normalized filter coefficients
    private float _b0 = 1f;
    private float _b1 = 0f;
    private float _b2 = 0f;
    private float _a1 = 0f;
    private float _a2 = 0f;

    // State history for Channel 0 (Left) and Channel 1 (Right)
    // Direct Form I Transposed state registers: s1, s2
    private float _s1L, _s2L;
    private float _s1R, _s2R;

    public AudioFilterType Type => _type;
    public float Frequency => _frequency;
    public float GainDb => _gainDb;
    public float Q => _q;

    public BiquadFilter(AudioFilterType type, float sampleRate = 48000f, float frequency = 1000f, float gainDb = 0f, float q = 1.414f)
    {
        _type = type;
        _sampleRate = Math.Max(8000f, sampleRate);
        _frequency = Math.Clamp(frequency, 10f, (_sampleRate * 0.49f));
        _gainDb = Math.Clamp(gainDb, -24f, 24f);
        _q = Math.Clamp(q, 0.1f, 20f);

        RecalculateCoefficients();
    }

    /// <summary>
    /// Updates filter parameters and recomputes transfer coefficients smoothly.
    /// </summary>
    public void Configure(AudioFilterType type, float frequency, float gainDb, float q, float sampleRate = 48000f)
    {
        _type = type;
        _sampleRate = Math.Max(8000f, sampleRate);
        _frequency = Math.Clamp(frequency, 10f, (_sampleRate * 0.49f));
        _gainDb = Math.Clamp(gainDb, -24f, 24f);
        _q = Math.Clamp(q, 0.1f, 20f);

        RecalculateCoefficients();
    }

    /// <summary>
    /// Resets internal state histories (clears delay registers).
    /// </summary>
    public void Reset()
    {
        _s1L = 0f;
        _s2L = 0f;
        _s1R = 0f;
        _s2R = 0f;
    }

    /// <summary>
    /// Processes interleaved stereo audio in-place.
    /// </summary>
    public void ProcessStereo(Span<float> samples)
    {
        // If filter is pure unity (e.g. peaking with 0 dB gain), bypass calculation
        if (_type == AudioFilterType.Peaking && MathF.Abs(_gainDb) < 0.01f)
        {
            return;
        }

        float b0 = _b0, b1 = _b1, b2 = _b2, a1 = _a1, a2 = _a2;
        float s1L = _s1L, s2L = _s2L;
        float s1R = _s1R, s2R = _s2R;

        int frameCount = samples.Length / 2;
        for (int i = 0; i < frameCount; i++)
        {
            int idxL = i * 2;
            int idxR = idxL + 1;

            float inL = samples[idxL];
            float inR = samples[idxR];

            // Protect against NaN/Inf inputs
            if (float.IsNaN(inL) || float.IsInfinity(inL)) inL = 0f;
            if (float.IsNaN(inR) || float.IsInfinity(inR)) inR = 0f;

            // Direct Form I Transposed calculation:
            // y[n] = b0 * x[n] + s1[n-1]
            // s1[n] = b1 * x[n] - a1 * y[n] + s2[n-1]
            // s2[n] = b2 * x[n] - a2 * y[n]

            // Left Channel
            float outL = b0 * inL + s1L;
            s1L = b1 * inL - a1 * outL + s2L;
            s2L = b2 * inL - a2 * outL;

            // Right Channel
            float outR = b0 * inR + s1R;
            s1R = b1 * inR - a1 * outR + s2R;
            s2R = b2 * inR - a2 * outR;

            // Denormal and NaN prevention
            if (MathF.Abs(s1L) < 1e-15f) s1L = 0f;
            if (MathF.Abs(s2L) < 1e-15f) s2L = 0f;
            if (MathF.Abs(s1R) < 1e-15f) s1R = 0f;
            if (MathF.Abs(s2R) < 1e-15f) s2R = 0f;

            samples[idxL] = outL;
            samples[idxR] = outR;
        }

        _s1L = s1L;
        _s2L = s2L;
        _s1R = s1R;
        _s2R = s2R;
    }

    private void RecalculateCoefficients()
    {
        float w0 = 2f * MathF.PI * _frequency / _sampleRate;
        float cosW0 = MathF.Cos(w0);
        float sinW0 = MathF.Sin(w0);
        float alpha = sinW0 / (2f * _q);

        float a0 = 1f, a1 = 0f, a2 = 0f;
        float b0 = 1f, b1 = 0f, b2 = 0f;

        switch (_type)
        {
            case AudioFilterType.Peaking:
                {
                    float A = MathF.Pow(10f, _gainDb / 40f);
                    b0 = 1f + alpha * A;
                    b1 = -2f * cosW0;
                    b2 = 1f - alpha * A;
                    a0 = 1f + alpha / A;
                    a1 = -2f * cosW0;
                    a2 = 1f - alpha / A;
                    break;
                }
            case AudioFilterType.LowPass:
                {
                    b0 = (1f - cosW0) / 2f;
                    b1 = 1f - cosW0;
                    b2 = (1f - cosW0) / 2f;
                    a0 = 1f + alpha;
                    a1 = -2f * cosW0;
                    a2 = 1f - alpha;
                    break;
                }
            case AudioFilterType.HighPass:
                {
                    b0 = (1f + cosW0) / 2f;
                    b1 = -(1f + cosW0);
                    b2 = (1f + cosW0) / 2f;
                    a0 = 1f + alpha;
                    a1 = -2f * cosW0;
                    a2 = 1f - alpha;
                    break;
                }
            case AudioFilterType.LowShelf:
                {
                    float A = MathF.Pow(10f, _gainDb / 40f);
                    float sqrtA2 = 2f * MathF.Sqrt(A) * alpha;
                    b0 = A * ((A + 1f) - (A - 1f) * cosW0 + sqrtA2);
                    b1 = 2f * A * ((A - 1f) - (A + 1f) * cosW0);
                    b2 = A * ((A + 1f) - (A - 1f) * cosW0 - sqrtA2);
                    a0 = (A + 1f) + (A - 1f) * cosW0 + sqrtA2;
                    a1 = -2f * ((A - 1f) + (A + 1f) * cosW0);
                    a2 = (A + 1f) + (A - 1f) * cosW0 - sqrtA2;
                    break;
                }
            case AudioFilterType.HighShelf:
                {
                    float A = MathF.Pow(10f, _gainDb / 40f);
                    float sqrtA2 = 2f * MathF.Sqrt(A) * alpha;
                    b0 = A * ((A + 1f) + (A - 1f) * cosW0 + sqrtA2);
                    b1 = -2f * A * ((A - 1f) + (A + 1f) * cosW0);
                    b2 = A * ((A + 1f) + (A - 1f) * cosW0 - sqrtA2);
                    a0 = (A + 1f) - (A - 1f) * cosW0 + sqrtA2;
                    a1 = 2f * ((A - 1f) - (A + 1f) * cosW0);
                    a2 = (A + 1f) - (A - 1f) * cosW0 - sqrtA2;
                    break;
                }
            case AudioFilterType.BandPass:
                {
                    b0 = alpha;
                    b1 = 0f;
                    b2 = -alpha;
                    a0 = 1f + alpha;
                    a1 = -2f * cosW0;
                    a2 = 1f - alpha;
                    break;
                }
        }

        // Normalize coefficients by a0
        if (MathF.Abs(a0) > 1e-12f)
        {
            float invA0 = 1f / a0;
            _b0 = b0 * invA0;
            _b1 = b1 * invA0;
            _b2 = b2 * invA0;
            _a1 = a1 * invA0;
            _a2 = a2 * invA0;
        }
        else
        {
            // Fallback safe unity pass-through
            _b0 = 1f; _b1 = 0f; _b2 = 0f;
            _a1 = 0f; _a2 = 0f;
        }
    }
}

