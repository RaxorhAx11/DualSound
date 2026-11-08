using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DualSound.Contracts.Models;

namespace DualSound.Core.Services;

/// <summary>
/// Service abstraction for enumerating Windows audio playback endpoints.
/// </summary>
public interface IAudioDeviceDiscoveryService
{
    /// <summary>
    /// Asynchronously enumerates all audio render (playback) endpoints.
    /// </summary>
    /// <param name="includeInactive">If true, includes unplugged, disabled, and not-present devices.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Read-only list of discovered audio devices.</returns>
    Task<IReadOnlyList<AudioDeviceInfo>> GetPlaybackDevicesAsync(bool includeInactive = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously retrieves an audio device by its Windows endpoint ID.
    /// </summary>
    /// <param name="deviceId">Windows CoreAudio endpoint identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>AudioDeviceInfo if found; otherwise null.</returns>
    Task<AudioDeviceInfo?> GetDeviceByIdAsync(string deviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously retrieves the current Windows default playback device.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>AudioDeviceInfo representing default playback device, or null if unavailable.</returns>
    Task<AudioDeviceInfo?> GetDefaultPlaybackDeviceAsync(CancellationToken cancellationToken = default);
}
