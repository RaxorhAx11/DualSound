namespace DualSound.Audio.Capture;

/// <summary>
/// Configuration parameters for the WASAPI real-time audio capture service.
/// </summary>
public sealed class AudioCaptureOptions
{
    /// <summary>
    /// Preferred internal target sample rate in Hz. Default is 48,000 Hz.
    /// </summary>
    public int TargetSampleRate { get; set; } = 48000;

    /// <summary>
    /// Preferred internal channel count. Default is 2 (Stereo).
    /// </summary>
    public int TargetChannels { get; set; } = 2;

    /// <summary>
    /// Low-latency WASAPI buffer latency in milliseconds. Default is 50 ms.
    /// </summary>
    public int BufferDurationMilliseconds { get; set; } = 50;

    /// <summary>
    /// Ring buffer capacity duration in seconds. Default is 1.0 second.
    /// </summary>
    public float RingBufferDurationSeconds { get; set; } = 1.0f;

    /// <summary>
    /// Whether to attempt automatic re-connection if the audio device is temporarily unplugged or interrupted.
    /// </summary>
    public bool AutoRestartOnDeviceLoss { get; set; } = true;

    /// <summary>
    /// Maximum number of restart attempts before transitioning to Error state.
    /// </summary>
    public int MaxRestartAttempts { get; set; } = 3;
}

