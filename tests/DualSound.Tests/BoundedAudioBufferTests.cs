using System;
using System.Threading.Tasks;
using DualSound.Audio.Capture;
using Xunit;

namespace DualSound.Tests;

public class BoundedAudioBufferTests
{
    [Fact]
    public void Buffer_InitialState_IsEmpty()
    {
        var buffer = new BoundedAudioBuffer(1000);

        Assert.Equal(1000, buffer.Capacity);
        Assert.Equal(0, buffer.AvailableSamples);
        Assert.Equal(0f, buffer.OccupancyPercentage);
        Assert.Equal(0, buffer.OverflowCount);
        Assert.Equal(0, buffer.TotalSamplesWritten);
    }

    [Fact]
    public void WriteAndRead_SequentialSamples_PreservesDataIntegrity()
    {
        var buffer = new BoundedAudioBuffer(100);
        var input = new float[] { 0.1f, -0.2f, 0.5f, -0.9f, 0.0f, 1.0f };

        var written = buffer.Write(input);
        Assert.Equal(input.Length, written);
        Assert.Equal(input.Length, buffer.AvailableSamples);

        var output = new float[input.Length];
        var read = buffer.Read(output);

        Assert.Equal(input.Length, read);
        Assert.Equal(0, buffer.AvailableSamples);
        Assert.Equal(input, output);
    }

    [Fact]
    public void OverflowHandling_DropsOldestSamples_TracksOverflowCount()
    {
        var buffer = new BoundedAudioBuffer(4);

        // Fill with [1, 2, 3, 4]
        buffer.Write(new float[] { 1f, 2f, 3f, 4f });
        Assert.Equal(4, buffer.AvailableSamples);
        Assert.Equal(1.0f, buffer.OccupancyPercentage);
        Assert.Equal(0, buffer.OverflowCount);

        // Write 2 more samples: [5, 6] -> Oldest [1, 2] dropped, buffer now [3, 4, 5, 6]
        buffer.Write(new float[] { 5f, 6f });
        Assert.Equal(4, buffer.AvailableSamples);
        Assert.Equal(1, buffer.OverflowCount);

        var output = new float[4];
        var read = buffer.Read(output);

        Assert.Equal(4, read);
        Assert.Equal(new float[] { 3f, 4f, 5f, 6f }, output);
    }

    [Fact]
    public void ClearAndReset_RestoresCleanState()
    {
        var buffer = new BoundedAudioBuffer(10);
        buffer.Write(new float[] { 0.5f, -0.5f });

        buffer.Clear();
        Assert.Equal(0, buffer.AvailableSamples);
        Assert.Equal(0f, buffer.OccupancyPercentage);

        buffer.Reset();
        Assert.Equal(0, buffer.TotalSamplesWritten);
        Assert.Equal(0, buffer.OverflowCount);
    }

    [Fact]
    public async Task ConcurrentWriteAndRead_ThreadSafe_NoExceptions()
    {
        var buffer = new BoundedAudioBuffer(2048);
        var iterations = 1000;
        var writingFinished = false;

        var writeTask = Task.Run(() =>
        {
            var chunk = new float[] { 0.1f, -0.1f, 0.2f, -0.2f };
            for (int i = 0; i < iterations; i++)
            {
                buffer.Write(chunk);
            }
            writingFinished = true;
        });

        var readTask = Task.Run(async () =>
        {
            var readBuffer = new float[4];
            int totalRead = 0;
            while (!writingFinished || buffer.AvailableSamples > 0)
            {
                var read = buffer.Read(readBuffer);
                totalRead += read;
                if (read == 0)
                {
                    await Task.Delay(1);
                }
            }
            return totalRead;
        });

        await Task.WhenAll(writeTask, readTask);
        Assert.True(buffer.TotalSamplesWritten == iterations * 4);
    }
}
