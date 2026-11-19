using System;
using System.Threading;
using DualSound.Contracts.Enums;
using DualSound.Contracts.Models;

namespace DualSound.Audio.Sync;

/// <summary>
/// High-precision bounded delay line and synchronization buffer for an individual audio output branch.
/// Stores interleaved stereo float audio frames and provides millisecond delay adjustment,
/// clock drift compensation, underrun/overrun protection, and smooth cross-fade delay transitions.
/// </summary>
public sealed class OutputDelayBuffer
{
    private readonly AudioOutputBranch _branch;
    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly float[] _buffer;
    private readonly int _capacitySamples;
    private readonly object _syncLock = new();

    private int _writeIndex = 0;
    private int _readIndex = 0;
    private int _availableSamples = 0;

    private float _targetDelayMs = 0f;
    private int _targetDelaySamples = 0;

    private long _totalFramesWritten = 0;
    private long _totalFramesRead = 0;
    private long _underruns = 0;
    private long _overruns = 0;
    private long _driftSamples = 0;

    // Smooth crossfade state for click-free live delay adjustments
    private int _fadeRemainingFrames = 0;
    private int _fadeTotalFrames = 0;
    private int _fadeOldReadIndex = 0;

    public AudioOutputBranch Branch => _branch;
    public float TargetDelayMs => _targetDelayMs;
    public long UnderrunCount => Interlocked.Read(ref _underruns);
    public long OverrunCount => Interlocked.Read(ref _overruns);
    public long DriftSamples => Interlocked.Read(ref _driftSamples);

    public float BufferOccupancyPercentage
    {
        get
        {
            lock (_syncLock)
            {
                return (float)_availableSamples / _capacitySamples * 100f;
            }
        }
    }

    /// <param name="branch">Target branch (TV HDMI or Home Theatre 3.5mm)</param>
    /// <param name="sampleRate">Sample rate (default 48000 Hz)</param>
    /// <param name="channels">Channel count (default 2 for stereo)</param>
    /// <param name="maxDelaySeconds">Maximum delay capability (default 1.0 second)</param>
    public OutputDelayBuffer(AudioOutputBranch branch, int sampleRate = 48000, int channels = 2, float maxDelaySeconds = 1.0f)
    {
        _branch = branch;
        _sampleRate = sampleRate;
        _channels = channels;

        // Capacity: maxDelaySeconds * sampleRate * channels
        _capacitySamples = (int)(Math.Max(0.5f, maxDelaySeconds) * _sampleRate * _channels);
        _buffer = new float[_capacitySamples];
    }

    /// <summary>
    /// Sets the desired synchronization delay in milliseconds (0 ms to 500 ms).
    /// Safe to call during active playback; transitions delay smoothly with raised-cosine crossfade.
    /// </summary>
    public void SetDelayMilliseconds(float delayMs)
    {
        lock (_syncLock)
        {
            int oldTargetSamples = _targetDelaySamples;
            _targetDelayMs = Math.Clamp(delayMs, 0f, 500f);
            int targetFrames = (int)(_targetDelayMs * 0.001f * _sampleRate);
            _targetDelaySamples = targetFrames * _channels;

            // If reducing delay while running, advance read pointer immediately to discard excess latency
            if (_targetDelaySamples < oldTargetSamples && _availableSamples > _targetDelaySamples)
            {
                int currentPlayable = Math.Max(0, _availableSamples - oldTargetSamples);
                int desiredAvailable = _targetDelaySamples + currentPlayable;
                if (_availableSamples > desiredAvailable)
                {
                    int drop = _availableSamples - desiredAvailable;
                    int oldRead = _readIndex;
                    _readIndex = (_readIndex + drop) % _capacitySamples;
                    _availableSamples = desiredAvailable;

                    // Smooth crossfade to prevent pops and clicks during live playback
                    if (_availableSamples > 0 && Interlocked.Read(ref _totalFramesRead) > 0)
                    {
                        _fadeOldReadIndex = oldRead;
                        _fadeTotalFrames = Math.Min(96, Math.Max(16, drop / _channels));
                        _fadeRemainingFrames = _fadeTotalFrames;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Smoothly adjusts delay by delta in milliseconds, strictly clamped between 0 and 500 ms.
    /// </summary>
    public void AdjustDelaySmoothly(float deltaMs)
    {
        SetDelayMilliseconds(_targetDelayMs + deltaMs);
    }

    /// <summary>
    /// Writes incoming interleaved stereo frames into the delay buffer.
    /// </summary>
    public void Write(ReadOnlySpan<float> inputSamples)
    {
        if (inputSamples.IsEmpty) return;

        lock (_syncLock)
        {
            int toWrite = inputSamples.Length;
            int freeSpace = _capacitySamples - _availableSamples;

            // Overflow protection: if free space is insufficient, drop oldest unread samples
            if (toWrite > freeSpace)
            {
                int dropSamples = toWrite - freeSpace;
                _readIndex = (_readIndex + dropSamples) % _capacitySamples;
                _availableSamples -= dropSamples;
                Interlocked.Add(ref _overruns, dropSamples / _channels);
            }

            int spaceToEnd = _capacitySamples - _writeIndex;
            if (toWrite <= spaceToEnd)
            {
                inputSamples.CopyTo(_buffer.AsSpan(_writeIndex, toWrite));
                _writeIndex = (_writeIndex + toWrite) % _capacitySamples;
            }
            else
            {
                inputSamples[..spaceToEnd].CopyTo(_buffer.AsSpan(_writeIndex, spaceToEnd));
                int remainder = toWrite - spaceToEnd;
                inputSamples.Slice(spaceToEnd, remainder).CopyTo(_buffer.AsSpan(0, remainder));
                _writeIndex = remainder;
            }

            _availableSamples += toWrite;
            Interlocked.Add(ref _totalFramesWritten, toWrite / _channels);
        }
    }

    /// <summary>
    /// Reads delayed stereo audio frames into the output destination buffer.
    /// If buffer has insufficient samples to satisfy the configured delay, outputs clean silence.
    /// </summary>
    public int Read(Span<float> outputBuffer)
    {
        if (outputBuffer.IsEmpty) return 0;

        lock (_syncLock)
        {
            int requestedSamples = outputBuffer.Length;

            // Check if we have accumulated enough samples to satisfy configured delay threshold
            int requiredThreshold = _targetDelaySamples;
            if (_availableSamples < requiredThreshold)
            {
                // Pre-buffering phase: output silence until delay threshold is accumulated
                outputBuffer.Clear();
                return outputBuffer.Length;
            }

            // Normal playback phase: determine how many samples are available beyond the delay line
            int playableSamples = _availableSamples - requiredThreshold;
            int samplesToRead = Math.Min(requestedSamples, playableSamples);

            if (samplesToRead <= 0)
            {
                // Starvation / Underrun: fill output with silence to prevent digital glitching
                outputBuffer.Clear();
                Interlocked.Add(ref _underruns, requestedSamples / _channels);
                return outputBuffer.Length;
            }

            int spaceToEnd = _capacitySamples - _readIndex;
            if (samplesToRead <= spaceToEnd)
            {
                _buffer.AsSpan(_readIndex, samplesToRead).CopyTo(outputBuffer[..samplesToRead]);
                _readIndex = (_readIndex + samplesToRead) % _capacitySamples;
            }
            else
            {
                _buffer.AsSpan(_readIndex, spaceToEnd).CopyTo(outputBuffer[..spaceToEnd]);
                int remainder = samplesToRead - spaceToEnd;
                _buffer.AsSpan(0, remainder).CopyTo(outputBuffer.Slice(spaceToEnd, remainder));
                _readIndex = remainder;
            }

            _availableSamples -= samplesToRead;
            Interlocked.Add(ref _totalFramesRead, samplesToRead / _channels);

            // Apply smooth crossfade to eliminate pops and clicks if delay transition is active
            if (_fadeRemainingFrames > 0 && samplesToRead > 0)
            {
                int framesInRead = samplesToRead / _channels;
                int framesToFade = Math.Min(_fadeRemainingFrames, framesInRead);

                for (int f = 0; f < framesToFade; f++)
                {
                    float t = 1.0f - ((float)_fadeRemainingFrames / _fadeTotalFrames);
                    // Raised-cosine crossfade: 0.5 * (1 - cos(pi * t))
                    float wNew = 0.5f * (1.0f - MathF.Cos(MathF.PI * t));
                    float wOld = 1.0f - wNew;

                    int bufBase = f * _channels;
                    for (int c = 0; c < _channels; c++)
                    {
                        float oldSample = _buffer[(_fadeOldReadIndex + c) % _capacitySamples];
                        float newSample = outputBuffer[bufBase + c];
                        outputBuffer[bufBase + c] = (wOld * oldSample) + (wNew * newSample);
                    }

                    _fadeOldReadIndex = (_fadeOldReadIndex + _channels) % _capacitySamples;
                    _fadeRemainingFrames--;
                }
            }

            // If requested more samples than were playable, pad the remainder with silence
            if (samplesToRead < requestedSamples)
            {
                outputBuffer[samplesToRead..].Clear();
                Interlocked.Add(ref _underruns, (requestedSamples - samplesToRead) / _channels);
            }

            // Drift tracking
            int targetOccupancy = _targetDelaySamples + requestedSamples;
            int drift = _availableSamples - targetOccupancy;
            Interlocked.Exchange(ref _driftSamples, drift / _channels);

            return outputBuffer.Length;
        }
    }

    /// <summary>
    /// Resets the buffer indices, clearing all delayed samples and counters.
    /// </summary>
    public void Reset()
    {
        lock (_syncLock)
        {
            Array.Clear(_buffer, 0, _buffer.Length);
            _writeIndex = 0;
            _readIndex = 0;
            _availableSamples = 0;
            _fadeRemainingFrames = 0;
            _fadeTotalFrames = 0;
            _fadeOldReadIndex = 0;
            Interlocked.Exchange(ref _underruns, 0);
            Interlocked.Exchange(ref _overruns, 0);
            Interlocked.Exchange(ref _driftSamples, 0);
        }
    }

    /// <summary>
    /// Gets a real-time status snapshot for UI telemetry.
    /// </summary>
    public AudioSyncStatus GetStatus(float estimatedDriverLatencyMs = 20f)
    {
        lock (_syncLock)
        {
            float occupancy = (float)_availableSamples / _capacitySamples * 100f;
            long underruns = UnderrunCount;
            long overruns = OverrunCount;
            long drift = DriftSamples;

            bool isSynced = underruns == 0 && Math.Abs(drift) < (_sampleRate * 0.05f); // within 50ms drift
            string health = "Synchronized";
            if (underruns > 0) health = "Underrun Detected";
            else if (overruns > 0) health = "Buffer Overrun";
            else if (Math.Abs(drift) > (_sampleRate * 0.03f)) health = "Clock Drift";

            return new AudioSyncStatus(
                Branch: _branch,
                ConfiguredDelayMs: _targetDelayMs,
                EstimatedLatencyMs: estimatedDriverLatencyMs + _targetDelayMs,
                BufferOccupancyPercentage: occupancy,
                DriftSamples: drift,
                UnderrunCount: underruns,
                OverrunCount: overruns,
                IsSynchronized: isSynced,
                HealthDescription: health);
        }
    }
}
