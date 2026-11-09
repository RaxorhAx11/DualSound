using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DualSound.Audio;
using DualSound.Contracts.Models;
using DualSound.Core.Services;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;

namespace DualSound.Infrastructure.Services;

/// <summary>
/// Discovers and enumerates Windows audio endpoints using NAudio and Windows CoreAudio MMDevice APIs.
/// Executes non-blocking COM operations on background threads and properly cleans up COM resources.
/// </summary>
public sealed class WindowsAudioDeviceDiscoveryService : IAudioDeviceDiscoveryService
{
    private readonly ILogger<WindowsAudioDeviceDiscoveryService> _logger;

    public WindowsAudioDeviceDiscoveryService(ILogger<WindowsAudioDeviceDiscoveryService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AudioDeviceInfo>> GetPlaybackDevicesAsync(bool includeInactive = true, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var results = new List<AudioDeviceInfo>();

            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var stateMask = includeInactive ? DeviceState.All : DeviceState.Active;

                var defaultPlaybackId = TryGetDefaultDeviceId(enumerator, Role.Multimedia) ??
                                         TryGetDefaultDeviceId(enumerator, Role.Console);
                var defaultCommunicationsId = TryGetDefaultDeviceId(enumerator, Role.Communications);

                var deviceCollection = enumerator.EnumerateAudioEndPoints(DataFlow.Render, stateMask);

                foreach (var device in deviceCollection)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    try
                    {
                        var info = AudioDeviceMapper.MapToDeviceInfo(
                            device,
                            defaultPlaybackId,
                            defaultCommunicationsId,
                            _logger);

                        results.Add(info);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to map audio device endpoint {DeviceId}", device.ID);
                    }
                }

                _logger.LogInformation("Enumerated {Count} Windows playback endpoints (includeInactive={IncludeInactive})",
                    results.Count, includeInactive);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enumerate Windows audio endpoints");
                throw;
            }

            // Return default device first, then active devices, then ordered by name
            return results
                .OrderByDescending(d => d.IsDefaultPlayback)
                .ThenByDescending(d => d.IsActive)
                .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AudioDeviceInfo?> GetDeviceByIdAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return null;
        }

        return await Task.Run(() =>
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var device = enumerator.GetDevice(deviceId);
                if (device == null)
                {
                    return null;
                }

                var defaultPlaybackId = TryGetDefaultDeviceId(enumerator, Role.Multimedia);
                var defaultCommunicationsId = TryGetDefaultDeviceId(enumerator, Role.Communications);

                return AudioDeviceMapper.MapToDeviceInfo(device, defaultPlaybackId, defaultCommunicationsId, _logger);
            }
            catch (COMException ex) when ((uint)ex.HResult == 0x80070490 || ex.HResult == unchecked((int)0x88890001))
            {
                // Element not found / Device missing
                _logger.LogDebug("Audio device {DeviceId} was not found", deviceId);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error retrieving audio device by ID {DeviceId}", deviceId);
                return null;
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AudioDeviceInfo?> GetDefaultPlaybackDeviceAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                var defaultDevice = TryGetDefaultDevice(enumerator, Role.Multimedia) ??
                                    TryGetDefaultDevice(enumerator, Role.Console);

                if (defaultDevice == null)
                {
                    return null;
                }

                var defaultCommunicationsId = TryGetDefaultDeviceId(enumerator, Role.Communications);

                return AudioDeviceMapper.MapToDeviceInfo(
                    defaultDevice,
                    defaultDevice.ID,
                    defaultCommunicationsId,
                    _logger);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error retrieving default playback device");
                return null;
            }
        }, cancellationToken);
    }

    private static MMDevice? TryGetDefaultDevice(MMDeviceEnumerator enumerator, Role role)
    {
        try
        {
            return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, role);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetDefaultDeviceId(MMDeviceEnumerator enumerator, Role role)
    {
        try
        {
            var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, role);
            return device?.ID;
        }
        catch
        {
            return null;
        }
    }
}

