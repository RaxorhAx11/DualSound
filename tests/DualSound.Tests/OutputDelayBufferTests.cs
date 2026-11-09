using System;
using DualSound.Audio.Sync;
using DualSound.Contracts.Enums;
using Xunit;

namespace DualSound.Tests;

public class OutputDelayBufferTests
{
    private const int SampleRate = 48000;
    private const int Channels = 2;

    [Fact]
    public void OutputDelayBuffer_ZeroDelay_ImmediatelyOutputsWrittenSamples()
    {
        var delayBuffer = new OutputDelayBuffer(AudioOutputBranch.TvHdmi, SampleRate, Channels);
        delayBuffer.SetDelayMilliseconds(0);

        var input = new float[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f };
        delayBuffer.Write(input.AsSpan());

        var output = new float[input.Length];
        int read = delayBuffer.Read(output.AsSpan());

        Assert.Equal(input.Length, read);
        for (int i = 0; i < input.Length; i++)
        {
            Assert.Equal(input[i], output[i], precision: 4);
        }
    }

    [Fact]
    public void OutputDelayBuffer_ConfiguredDelay_PreFillsSilenceBeforeAudio()
    {
        var delayBuffer = new OutputDelayBuffer(AudioOutputBranch.HomeTheatre35mm, SampleRate, Channels);

        // 10 ms delay = 480 frames = 960 stereo samples
        delayBuffer.SetDelayMilliseconds(10f);

        // Write only 480 samples (5 ms) -> not enough to satisfy 10 ms threshold
        var partialInput = new float[480];
        Array.Fill(partialInput, 0.7f);
        delayBuffer.Write(partialInput.AsSpan());

        var output = new float[480];
        delayBuffer.Read(output.AsSpan());

        // Pre-fill phase: must output clean silence
        foreach (var sample in output)
        {
            Assert.Equal(0.0f, sample);
        }

        // Now write another 960 samples to exceed the 10 ms delay threshold
        var secondChunk = new float[960];
        Array.Fill(secondChunk, 0.7f);
        delayBuffer.Write(secondChunk.AsSpan());

        var secondOutput = new float[480];
        delayBuffer.Read(secondOutput.AsSpan());

        // Now delayed audio should emerge
        Assert.Equal(0.7f, secondOutput[0], precision: 2);
    }

    [Fact]
    public void OutputDelayBuffer_Underrun_FillsWithSilenceAndTracksCounter()
    {
        var delayBuffer = new OutputDelayBuffer(AudioOutputBranch.TvHdmi, SampleRate, Channels);
        delayBuffer.SetDelayMilliseconds(0);

        // Buffer is empty, read 256 samples
        var output = new float[256];
        Array.Fill(output, 0.99f);

        int read = delayBuffer.Read(output.AsSpan());

        Assert.Equal(256, read);
        foreach (var s in output)
        {
            Assert.Equal(0.0f, s);
        }
        Assert.True(delayBuffer.UnderrunCount > 0, "Underrun counter must increment on buffer starvation");
    }

    [Fact]
    public void OutputDelayBuffer_CircularWrapAround_PreservesDataIntegrity()
    {
        // Small buffer capacity: 0.1s * 48000 * 2 = 9600 samples
        var delayBuffer = new OutputDelayBuffer(AudioOutputBranch.TvHdmi, SampleRate, Channels, maxDelaySeconds: 0.5f);
        delayBuffer.SetDelayMilliseconds(0);

        // Write and read chunks of 2000 samples 10 times (total 20,000 samples, wrapping around multiple times)
        for (int cycle = 0; cycle < 10; cycle++)
        {
            var chunk = new float[2000];
            for (int i = 0; i < chunk.Length; i++)
            {
                chunk[i] = (float)Math.Sin(i + cycle);
            }

            delayBuffer.Write(chunk.AsSpan());

            var readChunk = new float[2000];
            delayBuffer.Read(readChunk.AsSpan());

            for (int i = 0; i < chunk.Length; i++)
            {
                Assert.Equal(chunk[i], readChunk[i], precision: 4);
            }
        }
    }

    [Fact]
    public void OutputDelayBuffer_Reset_ResetsOccupancy()
    {
        var delayBuffer = new OutputDelayBuffer(AudioOutputBranch.TvHdmi, SampleRate, Channels);
        var input = new float[1000];
        Array.Fill(input, 0.5f);

        delayBuffer.Write(input.AsSpan());
        Assert.True(delayBuffer.BufferOccupancyPercentage > 0);

        delayBuffer.Reset();
        Assert.Equal(0.0f, delayBuffer.BufferOccupancyPercentage);
    }

    [Fact]
    public void OutputDelayBuffer_ReducingDelay_TrimsExcessSamplesImmediately()
    {
        var delayBuffer = new OutputDelayBuffer(AudioOutputBranch.TvHdmi, SampleRate, Channels);

        // Set initial delay to 50 ms (2400 frames = 4800 samples)
        delayBuffer.SetDelayMilliseconds(50f);

        // Write 4800 + 480 samples so we have delay + 1 playable packet
        var input = new float[4800 + 480];
        Array.Fill(input, 0.8f);
        delayBuffer.Write(input.AsSpan());

        // Now dynamically reduce delay to 10 ms (480 frames = 960 samples)
        delayBuffer.SetDelayMilliseconds(10f);

        // Next read of 480 samples should immediately be playable without waiting for buffer overflow
        var output = new float[480];
        delayBuffer.Read(output.AsSpan());

        Assert.Equal(0.8f, output[0], precision: 2);
    }
}
