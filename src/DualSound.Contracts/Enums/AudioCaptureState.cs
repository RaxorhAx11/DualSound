namespace DualSound.Contracts.Enums;

/// <summary>
/// Lifecycle state of the real-time WASAPI audio capture engine.
/// </summary>
public enum AudioCaptureState
{
    /// <summary>
    /// Capture engine is stopped and idle.
    /// </summary>
    Stopped = 0,

    /// <summary>
    /// Capture engine is initializing audio client and buffers.
    /// </summary>
    Starting = 1,

    /// <summary>
    /// Audio engine is actively streaming and processing real-time audio packets.
    /// </summary>
    Capturing = 2,

    /// <summary>
    /// Engine is flushing buffers and shutting down WASAPI capture.
    /// </summary>
    Stopping = 3,

    /// <summary>
    /// Capture was interrupted due to device disconnection or hardware state change.
    /// </summary>
    Interrupted = 4,

    /// <summary>
    /// An unrecoverable error occurred during capture.
    /// </summary>
    Error = 5
}

