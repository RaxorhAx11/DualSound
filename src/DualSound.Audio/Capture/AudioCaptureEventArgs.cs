using System;
using DualSound.Contracts.Enums;
using DualSound.Contracts.Models;

namespace DualSound.Audio.Capture;

/// <summary>
/// Event arguments for capture engine lifecycle state changes.
/// </summary>
public sealed class AudioCaptureStateChangedEventArgs : EventArgs
{
    public AudioCaptureState OldState { get; }
    public AudioCaptureState NewState { get; }
    public string? Message { get; }

    public AudioCaptureStateChangedEventArgs(AudioCaptureState oldState, AudioCaptureState newState, string? message = null)
    {
        OldState = oldState;
        NewState = newState;
        Message = message;
    }
}

/// <summary>
/// Event arguments for real-time audio level measurement updates.
/// </summary>
public sealed class AudioLevelsEventArgs : EventArgs
{
    public AudioLevels Levels { get; }

    public AudioLevelsEventArgs(AudioLevels levels)
    {
        Levels = levels;
    }
}

/// <summary>
/// Event arguments for audio capture errors or hardware invalidation.
/// </summary>
public sealed class AudioCaptureErrorEventArgs : EventArgs
{
    public string Message { get; }
    public Exception? Exception { get; }
    public bool IsRecoverable { get; }

    public AudioCaptureErrorEventArgs(string message, Exception? exception = null, bool isRecoverable = false)
    {
        Message = message;
        Exception = exception;
        IsRecoverable = isRecoverable;
    }
}

/// <summary>
/// Event arguments delivering real-time converted 32-bit float stereo audio packets.
/// </summary>
public sealed class AudioPacketEventArgs : EventArgs
{
    public ReadOnlyMemory<float> Samples { get; }
    public int FrameCount { get; }
    public int SampleRate { get; }
    public int Channels { get; }

    public AudioPacketEventArgs(ReadOnlyMemory<float> samples, int frameCount, int sampleRate = 48000, int channels = 2)
    {
        Samples = samples;
        FrameCount = frameCount;
        SampleRate = sampleRate;
        Channels = channels;
    }
}

