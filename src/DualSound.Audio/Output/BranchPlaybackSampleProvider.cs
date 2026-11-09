using System;
using DualSound.Audio.Sync;
using DualSound.Contracts.Enums;
using NAudio.Wave;

namespace DualSound.Audio.Output;

/// <summary>
/// Continuous pull-model sample provider feeding NAudio's WASAPI playback stream.
/// Pulls real-time stereo float frames from the synchronized OutputDelayBuffer.
/// When starvation occurs or when paused, outputs clean silence to avoid digital clicks.
/// </summary>
public sealed class BranchPlaybackSampleProvider : ISampleProvider
{
    private readonly OutputDelayBuffer _delayBuffer;
    private readonly WaveFormat _waveFormat;
    private bool _isPlaying = false;
    private readonly object _stateLock = new();

    public WaveFormat WaveFormat => _waveFormat;

    public bool IsPlaying
    {
        get
        {
            lock (_stateLock)
            {
                return _isPlaying;
            }
        }
        set
        {
            lock (_stateLock)
            {
                _isPlaying = value;
            }
        }
    }

    public BranchPlaybackSampleProvider(OutputDelayBuffer delayBuffer, int sampleRate = 48000)
    {
        _delayBuffer = delayBuffer;
        _waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        lock (_stateLock)
        {
            if (!_isPlaying)
            {
                Array.Clear(buffer, offset, count);
                return count;
            }
        }

        var span = buffer.AsSpan(offset, count);
        return _delayBuffer.Read(span);
    }

    public int Read(Span<float> buffer)
    {
        lock (_stateLock)
        {
            if (!_isPlaying)
            {
                buffer.Clear();
                return buffer.Length;
            }
        }

        return _delayBuffer.Read(buffer);
    }
}
