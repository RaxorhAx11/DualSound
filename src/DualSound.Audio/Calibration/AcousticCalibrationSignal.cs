using System;
using NAudio.Wave;

namespace DualSound.Audio.Calibration;

/// <summary>
/// Generates band-limited acoustic calibration chirps with smooth raised-cosine ramps
/// and strictly enforced safe amplitude limits.
/// </summary>
public static class AcousticCalibrationSignal
{
    public const int DefaultSampleRate = 48000;
    public const float DefaultStartFrequency = 400f;
    public const float DefaultEndFrequency = 3000f;
    public const float DefaultDurationSeconds = 0.20f; // 200 ms
    public const float DefaultAmplitude = 0.20f;       // -14 dBFS safe acoustic ceiling
    public const float RampDurationSeconds = 0.015f;    // 15 ms smooth cosine ramp

    /// <summary>
    /// Generates a normalized single-channel reference chirp for matched filter cross-correlation.
    /// Uses a band-limited logarithmic frequency sweep.
    /// </summary>
    public static float[] GenerateReferenceChirp(
        int sampleRate = DefaultSampleRate,
        float startFreq = DefaultStartFrequency,
        float endFreq = DefaultEndFrequency,
        float durationSeconds = DefaultDurationSeconds,
        float amplitude = DefaultAmplitude)
    {
        int totalSamples = (int)(durationSeconds * sampleRate);
        int rampSamples = (int)(RampDurationSeconds * sampleRate);
        float[] chirp = new float[totalSamples];

        double f0 = Math.Clamp(startFreq, 100f, 10000f);
        double f1 = Math.Clamp(endFreq, f0 + 100f, sampleRate * 0.45);
        double T = durationSeconds;
        float safeAmp = Math.Clamp(amplitude, 0.01f, 0.35f); // Hardware safety ceiling

        // Logarithmic chirp instantaneous phase:
        // phi(t) = 2 * pi * f0 * ( (k^t - 1) / ln(k) ), where k = (f1 / f0)^(1 / T)
        double k = Math.Pow(f1 / f0, 1.0 / T);
        double lnK = Math.Log(k);

        for (int i = 0; i < totalSamples; i++)
        {
            double t = (double)i / sampleRate;
            double phase = 2.0 * Math.PI * f0 * ((Math.Pow(k, t) - 1.0) / lnK);

            // Raised-cosine window envelope (15 ms ramp in, 15 ms ramp out)
            float envelope = 1.0f;
            if (i < rampSamples)
            {
                envelope = 0.5f * (1.0f - MathF.Cos(MathF.PI * i / rampSamples));
            }
            else if (i > totalSamples - rampSamples)
            {
                int remaining = totalSamples - i;
                envelope = 0.5f * (1.0f - MathF.Cos(MathF.PI * remaining / rampSamples));
            }

            chirp[i] = (float)(Math.Sin(phase) * safeAmp * envelope);
        }

        return chirp;
    }

    /// <summary>
    /// Generates an interleaved stereo chirp buffer for playback.
    /// </summary>
    public static float[] GenerateStereoChirp(
        int sampleRate = DefaultSampleRate,
        float startFreq = DefaultStartFrequency,
        float endFreq = DefaultEndFrequency,
        float durationSeconds = DefaultDurationSeconds,
        float amplitude = DefaultAmplitude)
    {
        float[] mono = GenerateReferenceChirp(sampleRate, startFreq, endFreq, durationSeconds, amplitude);
        float[] stereo = new float[mono.Length * 2];

        for (int i = 0; i < mono.Length; i++)
        {
            stereo[i * 2] = mono[i];     // Left
            stereo[i * 2 + 1] = mono[i]; // Right
        }

        return stereo;
    }

    /// <summary>
    /// Sample provider for streaming a calibration chirp to an audio endpoint.
    /// </summary>
    public sealed class ChirpSampleProvider : ISampleProvider
    {
        private readonly float[] _stereoSamples;
        private readonly WaveFormat _waveFormat;
        private int _position = 0;

        public WaveFormat WaveFormat => _waveFormat;
        public bool IsComplete => _position >= _stereoSamples.Length;

        public ChirpSampleProvider(float[] stereoSamples, int sampleRate = DefaultSampleRate)
        {
            _stereoSamples = stereoSamples;
            _waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(offset, count));
        }

        public int Read(Span<float> buffer)
        {
            int remaining = _stereoSamples.Length - _position;
            int toCopy = Math.Min(buffer.Length, remaining);

            if (toCopy <= 0)
            {
                buffer.Clear();
                return 0;
            }

            _stereoSamples.AsSpan(_position, toCopy).CopyTo(buffer[..toCopy]);
            _position += toCopy;

            if (toCopy < buffer.Length)
            {
                buffer[toCopy..].Clear();
            }

            return toCopy;
        }

        public void Reset()
        {
            _position = 0;
        }
    }
}

