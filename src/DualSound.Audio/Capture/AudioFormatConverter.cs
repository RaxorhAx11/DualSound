using System;
using System.Runtime.InteropServices;
using NAudio.Wave;

namespace DualSound.Audio.Capture;

/// <summary>
/// High-performance, allocation-conscious sample format converter.
/// Converts any Windows WASAPI input format (16-bit PCM, 24-bit PCM, 32-bit Float, Mono/Stereo/Multi-channel, 44.1k/48k/96k)
/// into the DualSound canonical internal format: 48,000 Hz, 32-bit IEEE Floating-Point, Stereo (2 channels).
/// Maintains phase continuity across packet boundaries during resampling.
/// </summary>
public sealed class AudioFormatConverter
{
    public const int TargetSampleRate = 48000;
    public const int TargetChannels = 2;

    private float[] _intermediateStereoBuffer = new float[16384];
    private float[] _resampledBuffer = new float[16384];

    // Inter-packet resampling phase continuity
    private float _lastSampleL;
    private float _lastSampleR;
    private double _resampleFraction;

    /// <summary>
    /// Converts incoming raw WASAPI bytes into normalized 48 kHz stereo 32-bit float samples.
    /// Returns the converted samples as a ReadOnlySpan of interleaved float values (L0, R0, L1, R1...).
    /// </summary>
    /// <param name="rawBuffer">Raw byte buffer from WASAPI DataAvailable event.</param>
    /// <param name="bytesRecorded">Valid byte count in buffer.</param>
    /// <param name="sourceFormat">NAudio WaveFormat of the capture endpoint.</param>
    /// <returns>ReadOnlySpan containing the 48 kHz stereo float samples.</returns>
    public ReadOnlySpan<float> ConvertToCanonicalStereo(byte[] rawBuffer, int bytesRecorded, WaveFormat sourceFormat)
    {
        if (bytesRecorded <= 0 || sourceFormat == null)
        {
            return ReadOnlySpan<float>.Empty;
        }

        var sourceChannels = Math.Max(1, sourceFormat.Channels);
        var sourceSampleRate = Math.Max(8000, sourceFormat.SampleRate);
        var bitsPerSample = sourceFormat.BitsPerSample;
        var isFloat = sourceFormat.Encoding == WaveFormatEncoding.IeeeFloat ||
                      (sourceFormat is WaveFormatExtensible ext && ext.SubFormat == AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT);

        // Calculate source frame count
        var bytesPerFrame = sourceFormat.BlockAlign > 0 ? sourceFormat.BlockAlign : (sourceChannels * bitsPerSample / 8);
        if (bytesPerFrame <= 0)
        {
            return ReadOnlySpan<float>.Empty;
        }

        var sourceFrames = bytesRecorded / bytesPerFrame;
        if (sourceFrames <= 0)
        {
            return ReadOnlySpan<float>.Empty;
        }

        // Ensure intermediate buffer is large enough (2 floats per frame)
        var neededStereoCapacity = sourceFrames * 2;
        if (_intermediateStereoBuffer.Length < neededStereoCapacity)
        {
            _intermediateStereoBuffer = new float[Math.Max(_intermediateStereoBuffer.Length * 2, neededStereoCapacity)];
        }

        // Step 1: Decode raw samples into stereo normalized float [-1.0f, 1.0f]
        DecodeToStereoFloats(rawBuffer, sourceFrames, sourceChannels, bitsPerSample, isFloat, _intermediateStereoBuffer);

        // Step 2: Resample to 48,000 Hz if necessary
        if (sourceSampleRate == TargetSampleRate)
        {
            // Already 48 kHz; save last samples for state continuity
            if (sourceFrames > 0)
            {
                _lastSampleL = _intermediateStereoBuffer[(sourceFrames - 1) * 2];
                _lastSampleR = _intermediateStereoBuffer[(sourceFrames - 1) * 2 + 1];
            }
            return _intermediateStereoBuffer.AsSpan(0, neededStereoCapacity);
        }

        // Resample using linear interpolation
        var resampledFrames = ResampleStereoLinear(
            _intermediateStereoBuffer.AsSpan(0, neededStereoCapacity),
            sourceFrames,
            sourceSampleRate,
            TargetSampleRate,
            ref _resampledBuffer);

        return _resampledBuffer.AsSpan(0, resampledFrames * 2);
    }

    private static void DecodeToStereoFloats(
        byte[] rawBuffer,
        int sourceFrames,
        int sourceChannels,
        int bitsPerSample,
        bool isFloat,
        float[] destination)
    {
        if (isFloat && bitsPerSample == 32)
        {
            var floatSpan = MemoryMarshal.Cast<byte, float>(rawBuffer.AsSpan());
            for (int i = 0; i < sourceFrames; i++)
            {
                int srcIdx = i * sourceChannels;
                float l = floatSpan[srcIdx];
                float r = sourceChannels > 1 ? floatSpan[srcIdx + 1] : l;

                destination[i * 2] = Math.Clamp(l, -1.0f, 1.0f);
                destination[i * 2 + 1] = Math.Clamp(r, -1.0f, 1.0f);
            }
        }
        else if (bitsPerSample == 16)
        {
            var shortSpan = MemoryMarshal.Cast<byte, short>(rawBuffer.AsSpan());
            const float scale16 = 1.0f / 32768.0f;
            for (int i = 0; i < sourceFrames; i++)
            {
                int srcIdx = i * sourceChannels;
                float l = shortSpan[srcIdx] * scale16;
                float r = sourceChannels > 1 ? shortSpan[srcIdx + 1] * scale16 : l;

                destination[i * 2] = Math.Clamp(l, -1.0f, 1.0f);
                destination[i * 2 + 1] = Math.Clamp(r, -1.0f, 1.0f);
            }
        }
        else if (bitsPerSample == 24)
        {
            const float scale24 = 1.0f / 8388608.0f;
            int bytesPerFrame = sourceChannels * 3;

            for (int i = 0; i < sourceFrames; i++)
            {
                int frameOffset = i * bytesPerFrame;
                int sampleLInt = (rawBuffer[frameOffset] |
                                 (rawBuffer[frameOffset + 1] << 8) |
                                 ((sbyte)rawBuffer[frameOffset + 2] << 16));
                float l = sampleLInt * scale24;

                float r = l;
                if (sourceChannels > 1)
                {
                    int rOffset = frameOffset + 3;
                    int sampleRInt = (rawBuffer[rOffset] |
                                     (rawBuffer[rOffset + 1] << 8) |
                                     ((sbyte)rawBuffer[rOffset + 2] << 16));
                    r = sampleRInt * scale24;
                }

                destination[i * 2] = Math.Clamp(l, -1.0f, 1.0f);
                destination[i * 2 + 1] = Math.Clamp(r, -1.0f, 1.0f);
            }
        }
        else if (bitsPerSample == 32) // 32-bit integer PCM
        {
            var intSpan = MemoryMarshal.Cast<byte, int>(rawBuffer.AsSpan());
            const float scale32 = 1.0f / 2147483648.0f;
            for (int i = 0; i < sourceFrames; i++)
            {
                int srcIdx = i * sourceChannels;
                float l = intSpan[srcIdx] * scale32;
                float r = sourceChannels > 1 ? intSpan[srcIdx + 1] * scale32 : l;

                destination[i * 2] = Math.Clamp(l, -1.0f, 1.0f);
                destination[i * 2 + 1] = Math.Clamp(r, -1.0f, 1.0f);
            }
        }
        else
        {
            // Fallback for 8-bit unsigned PCM
            for (int i = 0; i < sourceFrames; i++)
            {
                int srcIdx = i * sourceChannels;
                float l = (rawBuffer[srcIdx] - 128) / 128.0f;
                float r = sourceChannels > 1 ? (rawBuffer[srcIdx + 1] - 128) / 128.0f : l;

                destination[i * 2] = Math.Clamp(l, -1.0f, 1.0f);
                destination[i * 2 + 1] = Math.Clamp(r, -1.0f, 1.0f);
            }
        }
    }

    private int ResampleStereoLinear(
        ReadOnlySpan<float> sourceStereo,
        int sourceFrames,
        int sourceRate,
        int targetRate,
        ref float[] destBuffer)
    {
        double step = (double)sourceRate / targetRate;
        int estimatedOutputFrames = (int)Math.Ceiling(sourceFrames * (double)targetRate / sourceRate) + 2;

        int neededCapacity = estimatedOutputFrames * 2;
        if (destBuffer.Length < neededCapacity)
        {
            destBuffer = new float[Math.Max(destBuffer.Length * 2, neededCapacity)];
        }

        int outFrameCount = 0;
        double pos = _resampleFraction;

        while (pos < sourceFrames)
        {
            int index = (int)pos;
            float fraction = (float)(pos - index);

            float prevL, prevR;
            if (index == 0)
            {
                prevL = _lastSampleL;
                prevR = _lastSampleR;
            }
            else
            {
                prevL = sourceStereo[(index - 1) * 2];
                prevR = sourceStereo[(index - 1) * 2 + 1];
            }

            float nextL = sourceStereo[index * 2];
            float nextR = sourceStereo[index * 2 + 1];

            float outL = prevL + fraction * (nextL - prevL);
            float outR = prevR + fraction * (nextR - prevR);

            destBuffer[outFrameCount * 2] = outL;
            destBuffer[outFrameCount * 2 + 1] = outR;
            outFrameCount++;

            pos += step;
        }

        // Save state for next packet
        _resampleFraction = pos - sourceFrames;
        if (sourceFrames > 0)
        {
            _lastSampleL = sourceStereo[(sourceFrames - 1) * 2];
            _lastSampleR = sourceStereo[(sourceFrames - 1) * 2 + 1];
        }

        return outFrameCount;
    }

    /// <summary>
    /// Resets inter-packet continuity state.
    /// </summary>
    public void Reset()
    {
        _lastSampleL = 0f;
        _lastSampleR = 0f;
        _resampleFraction = 0.0;
    }
}

