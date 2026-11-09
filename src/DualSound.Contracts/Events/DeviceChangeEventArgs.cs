using System;
using DualSound.Contracts.Enums;

namespace DualSound.Contracts.Events;

/// <summary>
/// Type of audio device change notification received from Windows CoreAudio.
/// </summary>
public enum DeviceChangeType
{
    /// <summary>
    /// A new audio device has been plugged into or registered on the system.
    /// </summary>
    DeviceAdded,

    /// <summary>
    /// An audio device has been removed or unregistered from the system.
    /// </summary>
    DeviceRemoved,

    /// <summary>
    /// Operational state of an audio device changed (e.g. unplugged/plugged in, enabled/disabled).
    /// </summary>
    DeviceStateChanged,

    /// <summary>
    /// Windows default playback or communications endpoint was changed.
    /// </summary>
    DefaultDeviceChanged,

    /// <summary>
    /// An endpoint property changed (e.g. name or format).
    /// </summary>
    PropertyValueChanged
}

/// <summary>
/// Event arguments for Windows audio device state and lifecycle changes.
/// </summary>
public sealed class DeviceChangeEventArgs : EventArgs
{
    public DeviceChangeEventArgs(
        DeviceChangeType changeType,
        string deviceId,
        AudioDeviceState? newState = null,
        string? details = null)
    {
        ChangeType = changeType;
        DeviceId = deviceId;
        NewState = newState;
        Details = details;
        TimestampUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Type of change that occurred.
    /// </summary>
    public DeviceChangeType ChangeType { get; }

    /// <summary>
    /// CoreAudio endpoint identifier affected by this change.
    /// </summary>
    public string DeviceId { get; }

    /// <summary>
    /// New state of the device if applicable.
    /// </summary>
    public AudioDeviceState? NewState { get; }

    /// <summary>
    /// Optional human-readable details about the event.
    /// </summary>
    public string? Details { get; }

    /// <summary>
    /// UTC timestamp when event occurred.
    /// </summary>
    public DateTime TimestampUtc { get; }
}

