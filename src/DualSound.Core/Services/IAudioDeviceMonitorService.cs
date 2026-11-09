using System;
using DualSound.Contracts.Events;

namespace DualSound.Core.Services;

/// <summary>
/// Service abstraction for listening to real-time Windows audio hardware and endpoint events.
/// </summary>
public interface IAudioDeviceMonitorService : IDisposable
{
    /// <summary>
    /// Event raised when an audio device is added, removed, state changed, or default endpoint updated.
    /// </summary>
    event EventHandler<DeviceChangeEventArgs>? DeviceChanged;

    /// <summary>
    /// Starts listening to Windows audio endpoint notifications.
    /// </summary>
    void StartMonitoring();

    /// <summary>
    /// Stops listening to Windows audio endpoint notifications.
    /// </summary>
    void StopMonitoring();

    /// <summary>
    /// Indicates whether active monitoring is currently running.
    /// </summary>
    bool IsMonitoring { get; }
}

