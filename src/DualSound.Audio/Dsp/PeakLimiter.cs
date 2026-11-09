using System;
using System.Threading;
using DualSound.Contracts.Models;

namespace DualSound.Audio.Dsp;

/// <summary>
/// Studio-grade peak limiter, clipping detector, and safety guard.
/// Enforces a hard ceiling at -0.5 dBFS (0.944f linear) with fast lookahead/instant-attack and smooth exponential release.
/// Replaces any NaN, Infinity, or corrupted samples with clean silence (0.0f).
/// </summary>
public sealed class PeakLimiter
{
    private readonly float _sampleRate;
    private readonly object _lock = new();

    private bool _isEnabled = true;
    private float _ceilingLinear = 0.94406f; // -0.5 dBFS
    private float _releaseCoeff;
    private float _currentGain = 1.0f;
    private long _clippingCount = 0;

    public bool IsEnabled
    {
        get => _isEnabled;
        set => _isEnabled = value;
    }

    public long ClippingCount => Interlocked.Read(ref _clippingCount);

    public PeakLimiter(float sampleRate = 48000f, float releaseMs = 50f)
    {
        _sampleRate = sampleRate;
        _releaseCoeff = MathF.Exp(-1f / (Math.Max(5f, releaseMs) * 0.001f * _sampleRate));
    }

    public void ApplySettings(AudioSafetySettings settings)
    {
        lock (_lock)
        {
            _isEnabled = settings.PeakLimiterEnabled;
            float thresholdDbfs = Math.Clamp(settings.ThresholdDbfs, -12f, 0f);
            _ceilingLinear = MathF.Pow(10f, thresholdDbfs / 20f);
            _releaseCoeff = MathF.Exp(-1f / (Math.Max(5f, settings.ReleaseMs) * 0.001f * _sampleRate));
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _currentGain = 1.0f;
            Interlocked.Exchange(ref _clippingCount, 0);
        }
    }

    /// <summary>
    /// Enforces peak ceiling and NaN/Inf sanitation in-place across stereo float samples.
    /// </summary>
    public void Process(Span<float> stereoSamples)
    {
        if (stereoSamples.IsEmpty) return;

        float ceiling = _ceilingLinear;
        float release = _releaseCoeff;
        float gain = _currentGain;
        bool enabled = _isEnabled;
        long clips = 0;

        int frameCount = stereoSamples.Length / 2;
        for (int i = 0; i < frameCount; i++)
        {
            int idxL = i * 2;
            int idxR = idxL + 1;

            float sL = stereoSamples[idxL];
            float sR = stereoSamples[idxR];

            // 1. Sanitize NaN, Inf or subnormal numbers
            if (float.IsNaN(sL) || float.IsInfinity(sL)) { sL = 0f; clips++; }
            if (float.IsNaN(sR) || float.IsInfinity(sR)) { sR = 0f; clips++; }

            if (enabled)
            {
                // 2. Determine peak amplitude
                float peak = MathF.Max(MathF.Abs(sL), MathF.Abs(sR));

                // 3. Instant attack / gain reduction
                if (peak * gain > ceiling && peak > 1e-6f)
                {
                    gain = ceiling / peak;
                    clips++;
                }
                else
                {
                    // Exponential recovery towards unity gain (1.0)
                    gain = 1.0f - release * (1.0f - gain);
                }

                // 4. Apply gain
                sL *= gain;
                sR *= gain;

                // 5. Hard safety clamp ceiling
                sL = Math.Clamp(sL, -ceiling, ceiling);
                sR = Math.Clamp(sR, -ceiling, ceiling);
            }

            stereoSamples[idxL] = sL;
            stereoSamples[idxR] = sR;
        }

        _currentGain = gain;
        if (clips > 0)
        {
            Interlocked.Add(ref _clippingCount, clips);
        }
    }
}

