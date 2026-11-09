using System;
using DualSound.Audio.Dsp;
using DualSound.Contracts.Enums;
using DualSound.Contracts.Models;
using Xunit;

namespace DualSound.Tests;

public class DspFilterTests
{
    private const float SampleRate = 48000f;

    [Fact]
    public void BiquadFilter_PeakingEq_ZeroGainProducesUnityPassthrough()
    {
        var filter = new BiquadFilter(AudioFilterType.Peaking, SampleRate, 1000f, 0f, 1.414f);

        var samples = new float[] { 0.5f, 0.5f, -0.3f, -0.3f, 0.8f, 0.8f };
        var original = (float[])samples.Clone();

        filter.ProcessStereo(samples.AsSpan());

        for (int i = 0; i < samples.Length; i++)
        {
            Assert.Equal(original[i], samples[i], precision: 4);
        }
    }

    [Fact]
    public void BiquadFilter_PeakingEq_BoostAmplifiesTargetFrequency()
    {
        var filter = new BiquadFilter(AudioFilterType.Peaking, SampleRate, 1000f, 6f, 1.414f); // +6 dB boost at 1 kHz

        // Generate a 1 kHz sine wave
        var buffer = new float[960]; // 20 ms
        for (int i = 0; i < buffer.Length; i += 2)
        {
            float val = (float)Math.Sin(2 * Math.PI * 1000 * (i / 2) / SampleRate);
            buffer[i] = val;
            buffer[i + 1] = val;
        }

        var peakBefore = GetPeak(buffer);
        filter.ProcessStereo(buffer.AsSpan());
        var peakAfter = GetPeak(buffer);

        Assert.True(peakAfter > peakBefore * 1.5f, $"Expected boost from peak {peakBefore} to > 1.5x, got {peakAfter}");
    }

    [Fact]
    public void BiquadFilter_HandlesNanAndInfinityGracefully()
    {
        var filter = new BiquadFilter(AudioFilterType.Peaking, SampleRate, 1000f, 3f, 1.414f);

        var poisonedBuffer = new float[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0.5f };
        filter.ProcessStereo(poisonedBuffer.AsSpan());

        foreach (var sample in poisonedBuffer)
        {
            Assert.False(float.IsNaN(sample), "Filter output must not contain NaN");
            Assert.False(float.IsInfinity(sample), "Filter output must not contain Infinity");
        }
    }

    [Fact]
    public void NineBandEqualizer_FlatSettings_PassesAudioUnchanged()
    {
        var eq = new NineBandEqualizer((int)SampleRate);
        var settings = new EqualizerSettings { IsEnabled = true };

        eq.ApplySettings(settings);

        var buffer = new float[] { 0.25f, -0.25f, 0.5f, -0.5f, 0.1f, -0.1f };
        var expected = (float[])buffer.Clone();

        eq.Process(buffer.AsSpan());

        for (int i = 0; i < buffer.Length; i++)
        {
            Assert.Equal(expected[i], buffer[i], precision: 4);
        }
    }

    [Fact]
    public void NineBandEqualizer_Disabled_BypassesProcessing()
    {
        var eq = new NineBandEqualizer((int)SampleRate);
        var settings = new EqualizerSettings
        {
            IsEnabled = false,
            Bands = [new EqualizerBand { Frequency = 1000f, GainDb = 12f }]
        };

        eq.ApplySettings(settings);

        var buffer = new float[] { 0.4f, 0.4f, -0.4f, -0.4f };
        var original = (float[])buffer.Clone();

        eq.Process(buffer.AsSpan());

        for (int i = 0; i < buffer.Length; i++)
        {
            Assert.Equal(original[i], buffer[i]);
        }
    }

    [Fact]
    public void DialogueEnhancer_BoostsSpeechIntelligibilityBand()
    {
        var enhancer = new DialogueEnhancer(SampleRate);
        var settings = new DialogueEnhancementSettings
        {
            IsEnabled = true,
            EnhancementAmount = 0.8f,
            VoiceBoostDb = 6f,
            ClarityFrequency = 2500f,
            CompressionRatio = 2f
        };

        enhancer.ApplySettings(settings);

        // Generate 2.5 kHz speech formant tone
        var buffer = new float[960];
        for (int i = 0; i < buffer.Length; i += 2)
        {
            float val = 0.2f * (float)Math.Sin(2 * Math.PI * 2500 * (i / 2) / SampleRate);
            buffer[i] = val;
            buffer[i + 1] = val;
        }

        var peakBefore = GetPeak(buffer);
        enhancer.Process(buffer.AsSpan());
        var peakAfter = GetPeak(buffer);

        Assert.True(peakAfter > peakBefore, "Speech intelligibility formant must be amplified");
    }

    [Fact]
    public void BassProcessor_LowPassAttenuatesHighFrequencies()
    {
        var bass = new BassProcessor(SampleRate);
        var settings = new BassProcessingSettings
        {
            IsEnabled = true,
            LowPassFilterEnabled = true,
            CrossoverFrequency = 120f,
            BassGainDb = 0f,
            MonoSubwooferMix = false
        };

        bass.ApplySettings(settings);

        // 4 kHz high frequency tone (should be strongly attenuated by 120 Hz crossover)
        var buffer = new float[1920];
        for (int i = 0; i < buffer.Length; i += 2)
        {
            float val = 0.5f * (float)Math.Sin(2 * Math.PI * 4000 * (i / 2) / SampleRate);
            buffer[i] = val;
            buffer[i + 1] = val;
        }

        bass.Process(buffer.AsSpan());
        var peakAfter = GetPeak(buffer);

        Assert.True(peakAfter < 0.05f, $"Expected 4kHz tone to be attenuated below 0.05, got {peakAfter}");
    }

    [Fact]
    public void BassProcessor_MonoSubwooferMix_SumsChannelsEqually()
    {
        var bass = new BassProcessor(SampleRate);
        var settings = new BassProcessingSettings
        {
            IsEnabled = true,
            LowPassFilterEnabled = false,
            BassGainDb = 0f,
            MonoSubwooferMix = true
        };

        bass.ApplySettings(settings);

        // Left channel has 0.8f, Right channel has 0.0f
        var buffer = new float[] { 0.8f, 0.0f, 0.8f, 0.0f };
        bass.Process(buffer.AsSpan());

        // Subwoofer sum should mix both channels equally: (0.8 + 0.0) * 0.5 = 0.4
        Assert.Equal(buffer[0], buffer[1], precision: 2);
        Assert.True(buffer[0] > 0.3f && buffer[0] < 0.5f);
    }

    [Fact]
    public void PeakLimiter_ClampsExcessivePeaksToThreshold()
    {
        var limiter = new PeakLimiter(SampleRate);
        var settings = new AudioSafetySettings
        {
            PeakLimiterEnabled = true,
            ThresholdDbfs = -0.5f, // 0.944 linear ceiling
            ReleaseMs = 50f
        };

        limiter.ApplySettings(settings);

        // Intentionally hot signal (+6 dB over 0 dBFS ceiling = 2.0f)
        var hotBuffer = new float[] { 2.0f, -2.0f, 1.5f, -1.5f, 3.0f, -3.0f };
        limiter.Process(hotBuffer.AsSpan());

        var peak = GetPeak(hotBuffer);
        Assert.True(peak <= 0.945f, $"Peak must not exceed 0.944 ceiling, got {peak}");
    }

    [Fact]
    public void PeakLimiter_LeavesLowSignalsUnchanged()
    {
        var limiter = new PeakLimiter(SampleRate);
        limiter.ApplySettings(new AudioSafetySettings { PeakLimiterEnabled = true, ThresholdDbfs = -0.5f });

        var quietBuffer = new float[] { 0.2f, -0.2f, 0.1f, -0.1f };
        var original = (float[])quietBuffer.Clone();

        limiter.Process(quietBuffer.AsSpan());

        for (int i = 0; i < quietBuffer.Length; i++)
        {
            Assert.Equal(original[i], quietBuffer[i], precision: 3);
        }
    }

    private static float GetPeak(ReadOnlySpan<float> buffer)
    {
        float peak = 0f;
        for (int i = 0; i < buffer.Length; i++)
        {
            float abs = Math.Abs(buffer[i]);
            if (abs > peak) peak = abs;
        }
        return peak;
    }
}
