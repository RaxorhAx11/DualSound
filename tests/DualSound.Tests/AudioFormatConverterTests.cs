using System;
using System.Runtime.InteropServices;
using DualSound.Audio.Capture;
using NAudio.Wave;
using Xunit;

namespace DualSound.Tests;

public class AudioFormatConverterTests
{
    [Fact]
    public void ConvertIeeeFloat32Stereo48k_ReturnsDirectFloatSamples()
    {
        var converter = new AudioFormatConverter();
        var format = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

        var floatInput = new float[] { 0.25f, -0.75f, 0.5f, -0.5f };
        var byteBuffer = MemoryMarshal.AsBytes<float>(floatInput).ToArray();

        var converted = converter.ConvertToCanonicalStereo(byteBuffer, byteBuffer.Length, format);

        Assert.Equal(floatInput.Length, converted.Length);
        for (int i = 0; i < floatInput.Length; i++)
        {
            Assert.Equal(floatInput[i], converted[i], 4);
        }
    }

    [Fact]
    public void ConvertPcm16Stereo48k_ScalesToNormalizedFloats()
    {
        var converter = new AudioFormatConverter();
        var format = new WaveFormat(48000, 16, 2);

        // 32767 is ~1.0, -32768 is -1.0, 0 is 0.0, 16384 is 0.5
        var pcm16Input = new short[] { 32767, -32768, 0, 16384 };
        var byteBuffer = MemoryMarshal.AsBytes<short>(pcm16Input).ToArray();

        var converted = converter.ConvertToCanonicalStereo(byteBuffer, byteBuffer.Length, format);

        Assert.Equal(pcm16Input.Length, converted.Length);
        Assert.InRange(converted[0], 0.999f, 1.0f);
        Assert.Equal(-1.0f, converted[1]);
        Assert.Equal(0.0f, converted[2]);
        Assert.InRange(converted[3], 0.499f, 0.501f);
    }

    [Fact]
    public void ConvertPcm16Mono48k_ExpandsToStereo()
    {
        var converter = new AudioFormatConverter();
        var format = new WaveFormat(48000, 16, 1);

        var monoShorts = new short[] { 16384, -16384 };
        var byteBuffer = MemoryMarshal.AsBytes<short>(monoShorts).ToArray();

        var converted = converter.ConvertToCanonicalStereo(byteBuffer, byteBuffer.Length, format);

        // 2 mono frames -> 4 stereo samples (L0, R0, L1, R1)
        Assert.Equal(4, converted.Length);
        Assert.InRange(converted[0], 0.499f, 0.501f);
        Assert.InRange(converted[1], 0.499f, 0.501f); // Duplicate to Right
        Assert.InRange(converted[2], -0.501f, -0.499f);
        Assert.InRange(converted[3], -0.501f, -0.499f);
    }

    [Fact]
    public void ConvertPcm24Stereo48k_DecodesPackedBytesCorrectly()
    {
        var converter = new AudioFormatConverter();
        var format = new WaveFormat(48000, 24, 2);

        // Max positive 24-bit is 0x7FFFFF (8,388,607) -> ~1.0f
        // Min negative 24-bit is 0x800000 (-8,388,608) -> -1.0f
        var rawBytes = new byte[6];
        // L = 0x7FFFFF
        rawBytes[0] = 0xFF; rawBytes[1] = 0xFF; rawBytes[2] = 0x7F;
        // R = 0x800000
        rawBytes[3] = 0x00; rawBytes[4] = 0x00; rawBytes[5] = 0x80;

        var converted = converter.ConvertToCanonicalStereo(rawBytes, rawBytes.Length, format);

        Assert.Equal(2, converted.Length);
        Assert.InRange(converted[0], 0.999f, 1.0f);
        Assert.Equal(-1.0f, converted[1]);
    }

    [Fact]
    public void Resample44100HzTo48000Hz_ProducesExpectedSampleCount()
    {
        var converter = new AudioFormatConverter();
        var format = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);

        // 441 frames of 44.1kHz = 10ms -> should produce ~480 frames at 48kHz
        var inputFrames = 441;
        var floatInput = new float[inputFrames * 2];
        for (int i = 0; i < floatInput.Length; i++)
        {
            floatInput[i] = (float)Math.Sin(2 * Math.PI * 440 * (i / 2) / 44100.0);
        }
        var byteBuffer = MemoryMarshal.AsBytes<float>(floatInput).ToArray();

        var converted = converter.ConvertToCanonicalStereo(byteBuffer, byteBuffer.Length, format);

        int convertedFrames = converted.Length / 2;
        Assert.InRange(convertedFrames, 479, 482);
    }
}

