using System;
using NAudio.Wave;

namespace DualSound.Audio.Routing;

/// <summary>
/// Studio-grade test tone sample provider generating a smooth, band-limited stereo sine wave.
/// Features a gentle 50 ms raised-cosine ramp-in and ramp-out to completely prevent speaker pops,
/// and enforces a safe output amplitude ceiling.
/// </summary>
public sealed class SafeTestToneSampleProvider : ISampleProvider
{
    private readonly WaveFormat _waveFormat;
    private readonly float _frequency;
    private readonly float _amplitude;
    private readonly int _totalFrames;
    private readonly int _rampFrames;
    private int _framesRendered = 0;
    private double _phase = 0.0;

    public WaveFormat WaveFormat => _waveFormat;
    public bool IsComplete => _framesRendered >= _totalFrames;

    /// <param name="frequency">Frequency in Hz (default 440 Hz concert A or 1000 Hz reference).</param>
    /// <param name="durationSeconds">Tone duration in seconds.</param>
    /// <param name="amplitude">Linear amplitude ceiling (0.0 to 0.5, default 0.25f ~ -12 dBFS).</param>
    /// <param name="sampleRate">Sample rate in Hz (default 48000).</param>
    public SafeTestToneSampleProvider(float frequency = 440f, float durationSeconds = 3.0f, float amplitude = 0.25f, int sampleRate = 48000)
    {
        _waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);
        _frequency = Math.Clamp(frequency, 40f, 5000f);
        _amplitude = Math.Clamp(amplitude, 0.01f, 0.5f); // Strictly capped at 0.5f (-6 dBFS max) for acoustic safety
        _totalFrames = (int)(Math.Max(0.5f, durationSeconds) * sampleRate);
        _rampFrames = (int)(0.050f * sampleRate); // 50 ms cosine window ramp
    }

    public int Read(float[] buffer, int offset, int count)
    {
        return Read(buffer.AsSpan(offset, count));
    }

    public int Read(Span<float> buffer)
    {
        int requestedFrames = buffer.Length / 2;
        int remainingFrames = Math.Max(0, _totalFrames - _framesRendered);
        int framesToRender = Math.Min(requestedFrames, remainingFrames);

        if (framesToRender <= 0)
        {
            buffer.Clear();
            return 0;
        }

        double phaseStep = 2.0 * Math.PI * _frequency / _waveFormat.SampleRate;

        for (int i = 0; i < framesToRender; i++)
        {
            int currentFrame = _framesRendered + i;

            // Compute envelope window (smooth ramp up, sustain, smooth ramp down)
            float envelope = 1.0f;
            if (currentFrame < _rampFrames)
            {
                // Cosine fade-in: 0.5 * (1 - cos(pi * t / T))
                envelope = 0.5f * (1.0f - MathF.Cos(MathF.PI * currentFrame / _rampFrames));
            }
            else if (currentFrame > _totalFrames - _rampFrames)
            {
                // Cosine fade-out
                int framesFromEnd = _totalFrames - currentFrame;
                envelope = 0.5f * (1.0f - MathF.Cos(MathF.PI * framesFromEnd / _rampFrames));
            }

            float sample = (float)(Math.Sin(_phase) * _amplitude * envelope);
            _phase += phaseStep;
            if (_phase >= 2.0 * Math.PI) _phase -= 2.0 * Math.PI;

            int idx = i * 2;
            buffer[idx] = sample;     // Left
            buffer[idx + 1] = sample; // Right
        }

        _framesRendered += framesToRender;

        // Clear remainder if any
        if (framesToRender < requestedFrames)
        {
            buffer[(framesToRender * 2)..].Clear();
        }

        return framesToRender * 2;
    }
}

