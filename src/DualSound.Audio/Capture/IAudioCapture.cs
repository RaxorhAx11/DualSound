using System;
using System.Threading;
using System.Threading.Tasks;
using DualSound.Contracts.Enums;
using DualSound.Contracts.Models;

namespace DualSound.Audio.Capture;

/// <summary>
/// Core contract for the real-time Windows WASAPI audio capture service.
/// Captures audio from any selected recording endpoint, converts to canonical 48 kHz float stereo,
/// and feeds the internal bounded buffer and audio level monitors.
/// </summary>
public interface IAudioCapture : IDisposable
{
    /// <summary>
    /// Current lifecycle state of the capture engine.
    /// </summary>
    AudioCaptureState State { get; }

    /// <summary>
    /// Active capture options.
    /// </summary>
    AudioCaptureOptions Options { get; }

    /// <summary>
    /// Currently selected recording endpoint.
    /// </summary>
    AudioDeviceInfo? SelectedDevice { get; }

    /// <summary>
    /// Current real-time audio levels (peak, RMS, signal presence).
    /// </summary>
    AudioLevels CurrentLevels { get; }

    /// <summary>
    /// Underlying pre-allocated bounded sample ring buffer.
    /// </summary>
    BoundedAudioBuffer Buffer { get; }

    /// <summary>
    /// Cumulative count of audio frames captured.
    /// </summary>
    long TotalFramesCaptured { get; }

    /// <summary>
    /// Cumulative count of buffer overflow events.
    /// </summary>
    long BufferOverflows { get; }

    /// <summary>
    /// Current buffer occupancy ratio [0.0, 1.0].
    /// </summary>
    float BufferOccupancyPercentage { get; }

    /// <summary>
    /// Hardware sample rate of the capture endpoint.
    /// </summary>
    int SourceSampleRate { get; }

    /// <summary>
    /// Hardware channel count of the capture endpoint.
    /// </summary>
    int SourceChannels { get; }

    /// <summary>
    /// Human-readable format summary of the capture endpoint.
    /// </summary>
    string SourceFormatDescription { get; }

    /// <summary>
    /// Fired when capture lifecycle state transitions.
    /// </summary>
    event EventHandler<AudioCaptureStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Fired when audio meter levels are updated.
    /// </summary>
    event EventHandler<AudioLevelsEventArgs>? LevelsUpdated;

    /// <summary>
    /// Fired when a processed float audio packet is delivered.
    /// </summary>
    event EventHandler<AudioPacketEventArgs>? AudioPacketReceived;

    /// <summary>
    /// Fired when an audio capture error occurs.
    /// </summary>
    event EventHandler<AudioCaptureErrorEventArgs>? CaptureError;

    /// <summary>
    /// Assigns the target recording endpoint to capture from.
    /// </summary>
    /// <param name="device">Selected recording device.</param>
    void SetDevice(AudioDeviceInfo? device);

    /// <summary>
    /// Starts real-time WASAPI audio capture on the selected or default recording endpoint.
    /// </summary>
    /// <param name="deviceId">Optional device ID. If null, uses SelectedDevice or default recording device.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StartAsync(string? deviceId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops real-time audio capture and flushes buffers safely.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StopAsync(CancellationToken cancellationToken = default);
}

