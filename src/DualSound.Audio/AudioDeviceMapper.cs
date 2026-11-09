using System;
using DualSound.Contracts.Enums;
using DualSound.Contracts.Models;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;

namespace DualSound.Audio;

/// <summary>
/// Safely maps NAudio MMDevice instances into immutable AudioDeviceInfo contract records.
/// Performs comprehensive defensive COM property reading and exception handling.
/// </summary>
public static class AudioDeviceMapper
{
    public static AudioDeviceInfo MapToDeviceInfo(
        MMDevice device,
        string? defaultPlaybackId,
        string? defaultCommunicationsId,
        string? defaultRecordingId = null,
        AudioDataFlow dataFlow = AudioDataFlow.Render,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);

        var id = device.ID;
        var friendlyName = string.IsNullOrWhiteSpace(device.FriendlyName) ? "Unknown Device" : device.FriendlyName;
        var state = MapDeviceState(device.State);

        int? formFactor = null;
        var enumerator = string.Empty;
        var instanceId = string.Empty;
        var adapterName = string.Empty;
        var interfacePath = string.Empty;

        try
        {
            var props = device.Properties;
            if (props != null)
            {
                formFactor = ReadIntProperty(props, WindowsAudioPropertyKeys.FormFactor);
                enumerator = ReadStringProperty(props, WindowsAudioPropertyKeys.EnumeratorName);
                instanceId = ReadStringProperty(props, WindowsAudioPropertyKeys.DeviceInstanceId);
                adapterName = ReadStringProperty(props, WindowsAudioPropertyKeys.AdapterFriendlyName);
                interfacePath = ReadStringProperty(props, WindowsAudioPropertyKeys.InterfacePath);
            }
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Failed to read property store for audio device {DeviceId}", id);
        }

        var (deviceType, typeDescription) = AudioDeviceClassifier.Classify(
            formFactor,
            enumerator,
            instanceId,
            interfacePath,
            adapterName);

        var isDefaultPlayback = !string.IsNullOrEmpty(defaultPlaybackId) &&
                                string.Equals(id, defaultPlaybackId, StringComparison.OrdinalIgnoreCase);

        var isDefaultRecording = !string.IsNullOrEmpty(defaultRecordingId) &&
                                 string.Equals(id, defaultRecordingId, StringComparison.OrdinalIgnoreCase);

        var isDefaultCommunications = !string.IsNullOrEmpty(defaultCommunicationsId) &&
                                      string.Equals(id, defaultCommunicationsId, StringComparison.OrdinalIgnoreCase);

        var (channels, sampleRate, bitsPerSample) = TryReadAudioFormat(device, logger);

        return new AudioDeviceInfo
        {
            Id = id,
            Name = friendlyName,
            AdapterName = adapterName,
            State = state,
            DeviceType = deviceType,
            DeviceTypeDescription = typeDescription,
            FormFactor = formFactor,
            EnumeratorName = enumerator,
            InstanceId = instanceId,
            InterfacePath = interfacePath,
            IsDefaultPlayback = isDefaultPlayback,
            IsDefaultRecording = isDefaultRecording,
            IsDefaultCommunications = isDefaultCommunications,
            DataFlow = dataFlow,
            Channels = channels,
            SampleRate = sampleRate,
            BitsPerSample = bitsPerSample
        };
    }

    private static AudioDeviceState MapDeviceState(DeviceState state) => state switch
    {
        DeviceState.Active => AudioDeviceState.Active,
        DeviceState.Disabled => AudioDeviceState.Disabled,
        DeviceState.NotPresent => AudioDeviceState.NotPresent,
        DeviceState.Unplugged => AudioDeviceState.Unplugged,
        _ => AudioDeviceState.NotPresent
    };

    private static (int Channels, int SampleRate, int BitsPerSample) TryReadAudioFormat(MMDevice device, ILogger? logger)
    {
        // Only active endpoints can reliably initialize audio clients to query current mix format
        if (device.State != DeviceState.Active)
        {
            return (0, 0, 0);
        }

        try
        {
            using var client = device.CreateAudioClient();
            var mixFormat = client?.MixFormat;
            if (mixFormat != null)
            {
                return (mixFormat.Channels, mixFormat.SampleRate, mixFormat.BitsPerSample);
            }
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Unable to query mix format for active device {DeviceId}", device.ID);
        }

        return (0, 0, 0);
    }

    private static int? ReadIntProperty(PropertyStore props, PropertyKey key)
    {
        try
        {
            if (props.Contains(key))
            {
                var val = props[key].Value;
                if (val is int i) return i;
                if (val is uint u) return (int)u;
                if (int.TryParse(val?.ToString(), out var parsed)) return parsed;
            }
        }
        catch { }
        return null;
    }

    private static string ReadStringProperty(PropertyStore props, PropertyKey key)
    {
        try
        {
            if (props.Contains(key))
            {
                return props[key].Value?.ToString() ?? string.Empty;
            }
        }
        catch { }
        return string.Empty;
    }
}
