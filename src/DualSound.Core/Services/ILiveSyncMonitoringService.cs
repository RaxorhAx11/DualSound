using System;
using DualSound.Contracts.Models;

namespace DualSound.Core.Services;

/// <summary>
/// Service managing continuous live synchronization monitoring and gradual clock drift compensation
/// between TV and Home Theatre audio output devices during long playback sessions.
/// </summary>
public interface ILiveSyncMonitoringService : IDisposable
{
    /// <summary>
    /// Gets or sets whether continuous live sync monitoring is enabled.
    /// </summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// Gets current live synchronization status and metrics.
    /// </summary>
    LiveSyncStatus CurrentStatus { get; }

    /// <summary>
    /// Raised when live monitoring status, drift estimates, or telemetry update.
    /// </summary>
    event EventHandler<LiveSyncStatus>? StatusChanged;

    /// <summary>
    /// Starts monitoring telemetry.
    /// </summary>
    void Start();

    /// <summary>
    /// Stops monitoring.
    /// </summary>
    void Stop();

    /// <summary>
    /// Resets drift accumulators and metrics.
    /// </summary>
    void Reset();
}

