using System;
using System.Threading;
using DualSound.Contracts.Enums;
using DualSound.Contracts.Models;
using DualSound.Core.Services;

namespace DualSound.Audio.Sync;

/// <summary>
/// Production implementation of the multi-branch audio synchronization manager.
/// Coordinates independent delay lines for TV HDMI and Home Theatre 3.5mm outputs.
/// </summary>
public sealed class AudioSyncManager : IAudioSyncManager
{
    private readonly OutputDelayBuffer _tvDelayBuffer;
    private readonly OutputDelayBuffer _htDelayBuffer;
    private readonly Timer? _telemetryTimer;
    private bool _isDisposed;

    public OutputDelayBuffer TvDelayBuffer => _tvDelayBuffer;
    public OutputDelayBuffer HomeTheatreDelayBuffer => _htDelayBuffer;

    public event EventHandler<AudioSyncStatus>? StatusUpdated;

    public AudioSyncManager(int sampleRate = 48000)
    {
        _tvDelayBuffer = new OutputDelayBuffer(AudioOutputBranch.TvHdmi, sampleRate);
        _htDelayBuffer = new OutputDelayBuffer(AudioOutputBranch.HomeTheatre35mm, sampleRate);

        // Periodic sync telemetry timer at ~10 Hz (100 ms)
        _telemetryTimer = new Timer(OnTelemetryTimerTick, null, 100, 100);
    }

    public OutputDelayBuffer GetBuffer(AudioOutputBranch branch) => branch switch
    {
        AudioOutputBranch.TvHdmi => _tvDelayBuffer,
        AudioOutputBranch.HomeTheatre35mm => _htDelayBuffer,
        _ => throw new ArgumentOutOfRangeException(nameof(branch))
    };

    public void SetDelay(AudioOutputBranch branch, float delayMs)
    {
        GetBuffer(branch).SetDelayMilliseconds(delayMs);
    }

    public float GetDelay(AudioOutputBranch branch)
    {
        return GetBuffer(branch).TargetDelayMs;
    }

    public AudioSyncStatus GetStatus(AudioOutputBranch branch)
    {
        return GetBuffer(branch).GetStatus();
    }

    public void Reset()
    {
        _tvDelayBuffer.Reset();
        _htDelayBuffer.Reset();
    }

    private void OnTelemetryTimerTick(object? state)
    {
        if (_isDisposed) return;

        try
        {
            var tvStatus = _tvDelayBuffer.GetStatus();
            var htStatus = _htDelayBuffer.GetStatus();

            StatusUpdated?.Invoke(this, tvStatus);
            StatusUpdated?.Invoke(this, htStatus);
        }
        catch { }
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            _telemetryTimer?.Dispose();
        }
    }
}

