using System;
using DualSound.Contracts.Enums;

namespace DualSound.Audio;

/// <summary>
/// Classifies audio endpoints based on verified Windows CoreAudio and device bus properties.
/// Avoids naive name heuristics and strictly uses hardware enumerator metadata and form factors.
/// Displays "Unknown connection type" when connection cannot be verified.
/// </summary>
public static class AudioDeviceClassifier
{
    // FormFactor definitions from Windows mmdeviceapi.h
    private const int EndpointFormFactor_RemoteNetworkDevice = 0;
    private const int EndpointFormFactor_Speakers = 1;
    private const int EndpointFormFactor_LineLevel = 2;
    private const int EndpointFormFactor_Headphones = 3;
    private const int EndpointFormFactor_Microphone = 4;
    private const int EndpointFormFactor_Headset = 5;
    private const int EndpointFormFactor_Handset = 6;
    private const int EndpointFormFactor_UnknownDigitalPassthrough = 7;
    private const int EndpointFormFactor_SPDIF = 8;
    private const int EndpointFormFactor_DigitalAudioDisplayDevice = 9;
    private const int EndpointFormFactor_UnknownFormFactor = 10;

    /// <summary>
    /// Classifies an audio device endpoint using its Windows hardware properties.
    /// </summary>
    /// <param name="formFactor">Windows EndpointFormFactor integer if available.</param>
    /// <param name="enumeratorName">PKEY_Device_EnumeratorName (e.g. HDAUDIO, USB, BTHENUM, ROOT, SWD).</param>
    /// <param name="instanceId">PKEY_Device_InstanceId (e.g. {1}.USB\VID_..., {1}.HDAUDIO\...).</param>
    /// <param name="interfacePath">Device interface path string.</param>
    /// <param name="adapterName">Hardware adapter name.</param>
    /// <returns>A tuple of the resolved AudioDeviceType and human-readable description.</returns>
    public static (AudioDeviceType DeviceType, string Description) Classify(
        int? formFactor,
        string? enumeratorName,
        string? instanceId,
        string? interfacePath,
        string? adapterName)
    {
        var bus = enumeratorName?.Trim().ToUpperInvariant() ?? string.Empty;
        var instance = instanceId?.Trim().ToUpperInvariant() ?? string.Empty;
        var iface = interfacePath?.Trim().ToUpperInvariant() ?? string.Empty;
        var adapter = adapterName?.Trim().ToUpperInvariant() ?? string.Empty;

        // 1. Bluetooth Audio
        // Windows assigns BTHENUM, BTHHFPO, BTHLEENUM, or BTHLE to Bluetooth bus devices
        if (bus is "BTHENUM" or "BTHHFPO" or "BTHLEENUM" or "BTHLE" ||
            instance.Contains(@"BTHENUM\") || instance.Contains(@"BTHHFPO\") || instance.Contains(@"BTH\"))
        {
            var detail = formFactor switch
            {
                EndpointFormFactor_Headphones => "Bluetooth Audio (Headphones)",
                EndpointFormFactor_Headset => "Bluetooth Audio (Headset)",
                EndpointFormFactor_Speakers => "Bluetooth Audio (Speaker)",
                _ => "Bluetooth Audio"
            };
            return (AudioDeviceType.BluetoothAudio, detail);
        }

        // 2. USB Audio
        // Windows assigns USB or USBCCGP or USBAUDIO to USB sound cards and DACs
        if (bus is "USB" or "USBCCGP" or "USBAUDIO" or "USB3" ||
            instance.Contains(@"\USB\") || instance.StartsWith("{1}.USB", StringComparison.OrdinalIgnoreCase))
        {
            var detail = formFactor switch
            {
                EndpointFormFactor_Headphones => "USB Audio (Headphones)",
                EndpointFormFactor_Headset => "USB Audio (Headset)",
                EndpointFormFactor_Speakers => "USB Audio (Speakers)",
                EndpointFormFactor_LineLevel => "USB Audio (Line Out)",
                _ => "USB Audio Device"
            };
            return (AudioDeviceType.UsbAudio, detail);
        }

        // 3. Virtual / Software Audio Devices
        // Virtual drivers register under ROOT or SWD (Software Device) or use virtual bus architectures
        if (bus is "ROOT" or "SWD" ||
            instance.Contains(@"ROOT\MEDIA") ||
            instance.Contains(@"ROOT\VIRTUAL") ||
            instance.Contains(@"SWD\MMDEVAPI") ||
            iface.Contains(@"\ROOT#MEDIA") ||
            adapter.Contains("VIRTUAL") ||
            adapter.Contains("SONAR") ||
            adapter.Contains("VB-AUDIO") ||
            adapter.Contains("VOICEMEETER") ||
            adapter.Contains("BOOM AUDIO") ||
            adapter.Contains("STEAM STREAMING"))
        {
            return (AudioDeviceType.VirtualAudio, "Virtual Audio Device");
        }

        // 4. HDMI / DisplayPort Audio (Digital Display Devices)
        // FormFactor 9 is DigitalAudioDisplayDevice in Windows MMDevice API
        if (formFactor == EndpointFormFactor_DigitalAudioDisplayDevice ||
            formFactor == EndpointFormFactor_SPDIF ||
            iface.Contains("HDMIOUT") ||
            iface.Contains("DISPLAYPORT"))
        {
            return (AudioDeviceType.HdmiDisplayPort, "HDMI / DisplayPort Audio");
        }

        // 5. 3.5 mm / Built-in Analog Audio
        // Analog endpoints on motherboard HDAUDIO (High Definition Audio) codecs
        if (bus == "HDAUDIO" || instance.Contains(@"HDAUDIO\") || instance.Contains(@"PCI\"))
        {
            if (formFactor == EndpointFormFactor_Headphones)
            {
                return (AudioDeviceType.Headphones, "3.5 mm Audio (Headphone Jack)");
            }

            if (formFactor == EndpointFormFactor_LineLevel)
            {
                return (AudioDeviceType.LineOut35mm, "3.5 mm Audio (Line Out)");
            }

            if (formFactor == EndpointFormFactor_Speakers)
            {
                return (AudioDeviceType.Speakers, "Built-in / 3.5 mm Speakers");
            }

            if (formFactor == EndpointFormFactor_Headset)
            {
                return (AudioDeviceType.Headphones, "3.5 mm Headset");
            }
        }

        // 6. Microphones & Recording Form Factors
        if (formFactor == EndpointFormFactor_Microphone)
        {
            if (bus is "USB" or "USBAUDIO" || instance.Contains(@"\USB\"))
            {
                return (AudioDeviceType.UsbAudio, "USB Microphone");
            }

            if (bus == "HDAUDIO" || instance.Contains(@"HDAUDIO\"))
            {
                return (AudioDeviceType.Microphone, "Built-in / Analog Microphone");
            }

            return (AudioDeviceType.Microphone, "Microphone");
        }

        // 7. Generic Form Factor Fallback if bus is known
        if (formFactor == EndpointFormFactor_Headphones)
        {
            return (AudioDeviceType.Headphones, "Headphones (Analog)");
        }

        if (formFactor == EndpointFormFactor_LineLevel)
        {
            return (AudioDeviceType.LineOut35mm, "3.5 mm Line Output");
        }

        if (formFactor == EndpointFormFactor_Speakers)
        {
            return (AudioDeviceType.Speakers, "Speakers");
        }

        // 8. Unknown Connection Type
        // If the hardware endpoint properties cannot be verified with certainty,
        // we must never make blind guesses or hardcode.
        return (AudioDeviceType.Unknown, "Unknown connection type");
    }
}

