using System;
using DualSound.Audio.Capture;
using Xunit;

namespace DualSound.Tests;

public class AudioLevelMeterTests
{
    [Fact]
    public void ProcessSilence_ReturnsZeroLevelsAndNoSignal()
    {
        var meter = new AudioLevelMeter();
        var silence = new float[256];

        meter.ProcessSamples(silence);
        var levels = meter.CurrentLevels;

        Assert.Equal(0f, levels.LeftPeak);
        Assert.Equal(0f, levels.RightPeak);
        Assert.Equal(0f, levels.LeftRms);
        Assert.Equal(0f, levels.RightRms);
        Assert.False(levels.HasSignal);
        Assert.Equal(-60f, levels.MasterPeakDb);
    }

    [Fact]
    public void ProcessFullScaleSineWave_CalculatesPeakAndRmsAccurately()
    {
        var meter = new AudioLevelMeter();
        int frames = 1000;
        var samples = new float[frames * 2];

        // 1 kHz sine wave with amplitude 1.0 (Peak = 1.0, theoretical RMS for sine = 1 / sqrt(2) ~= 0.707)
        for (int i = 0; i < frames; i++)
        {
            float s = (float)Math.Sin(2 * Math.PI * 1000 * i / 48000.0);
            samples[i * 2] = s;     // Left
            samples[i * 2 + 1] = s; // Right
        }

        meter.ProcessSamples(samples);
        var levels = meter.CurrentLevels;

        Assert.InRange(levels.LeftPeak, 0.99f, 1.0f);
        Assert.InRange(levels.RightPeak, 0.99f, 1.0f);
        Assert.InRange(levels.LeftRms, 0.69f, 0.72f);
        Assert.InRange(levels.RightRms, 0.69f, 0.72f);
        Assert.True(levels.HasSignal);
        Assert.InRange(levels.MasterPeakDb, -0.1f, 0.0f);
    }

    [Fact]
    public void DetectsSignal_OnlyWhenAboveNoiseThreshold()
    {
        var meter = new AudioLevelMeter();

        // Very faint noise below -60 dBFS (amplitude < 0.001)
        var lowNoise = new float[] { 0.0005f, 0.0005f, -0.0005f, -0.0005f };
        meter.ProcessSamples(lowNoise);
        Assert.False(meter.CurrentLevels.HasSignal);

        // Audible signal (-20 dBFS, amplitude 0.1)
        var audibleSignal = new float[] { 0.1f, 0.1f, -0.1f, -0.1f };
        meter.ProcessSamples(audibleSignal);
        Assert.True(meter.CurrentLevels.HasSignal);
    }

    [Fact]
    public void BallisticsDecay_SmoothlyReleasesMeters()
    {
        var meter = new AudioLevelMeter();

        // Hit full scale peak
        meter.ProcessSamples(new float[] { 1.0f, 1.0f });
        Assert.Equal(1.0f, meter.CurrentLevels.LeftPeak);

        // Next frame is silence; peak should decay smoothly (not drop immediately to 0)
        meter.ProcessSamples(new float[] { 0.0f, 0.0f });
        var decayed = meter.CurrentLevels.LeftPeak;

        Assert.InRange(decayed, 0.75f, 0.90f);
    }
}

