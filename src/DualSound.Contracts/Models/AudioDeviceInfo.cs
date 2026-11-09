using DualSound.Contracts.Enums;

namespace DualSound.Contracts.Models;

/// <summary>
/// Immutable snapshot model representing an enumerated Windows audio endpoint.
/// </summary>
public sealed record AudioDeviceInfo
{
    /// <summary>
    /// Unique Windows CoreAudio endpoint identifier (e.g. {0.0.0.00000000}.{...}).
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Friendly display name of the device (e.g. "Speakers (Realtek(R) Audio)").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Hardware audio adapter or sound card name (e.g. "Realtek(R) Audio", "AMD High Definition Audio Device").
    /// </summary>
    public string AdapterName { get; init; } = string.Empty;

    /// <summary>
    /// Operational state of the device (Active, Disabled, NotPresent, Unplugged).
    /// </summary>
    public AudioDeviceState State { get; init; } = AudioDeviceState.Active;

    /// <summary>
    /// Hardware connection category reliably identified from Windows endpoint metadata.
    /// </summary>
    public AudioDeviceType DeviceType { get; init; } = AudioDeviceType.Unknown;

    /// <summary>
    /// Human-friendly connection type description. Displays "Unknown connection type" if unverified.
    /// </summary>
    public string DeviceTypeDescription { get; init; } = "Unknown connection type";

    /// <summary>
    /// Windows Endpoint FormFactor value (e.g. Speakers=1, LineLevel=2, Headphones=3, DigitalAudioDisplay=9).
    /// </summary>
    public int? FormFactor { get; init; }

    /// <summary>
    /// Windows bus enumerator name (e.g. "HDAUDIO", "USB", "BTHENUM", "ROOT", "SWD").
    /// </summary>
    public string EnumeratorName { get; init; } = string.Empty;

    /// <summary>
    /// Windows device instance identifier (e.g. "{1}.HDAUDIO\...").
    /// </summary>
    public string InstanceId { get; init; } = string.Empty;

    /// <summary>
    /// Windows device interface path.
    /// </summary>
    public string InterfacePath { get; init; } = string.Empty;

    /// <summary>
    /// Indicates whether this device is currently the Windows default playback endpoint (Multimedia/Console).
    /// </summary>
    public bool IsDefaultPlayback { get; init; }

    /// <summary>
    /// Indicates whether this device is currently the Windows default recording endpoint (Multimedia/Console).
    /// </summary>
    public bool IsDefaultRecording { get; init; }

    /// <summary>
    /// Indicates whether this device is currently the Windows default communications endpoint.
    /// </summary>
    public bool IsDefaultCommunications { get; init; }

    /// <summary>
    /// Audio data flow direction (Render for playback, Capture for recording).
    /// </summary>
    public AudioDataFlow DataFlow { get; init; } = AudioDataFlow.Render;

    /// <summary>
    /// Number of audio channels (e.g. 2 for Stereo, 6 for 5.1 Surround, 8 for 7.1).
    /// </summary>
    public int Channels { get; init; }

    /// <summary>
    /// Current sample rate in Hz (e.g. 44100, 48000, 96000, 192000).
    /// </summary>
    public int SampleRate { get; init; }

    /// <summary>
    /// Bit depth per sample (e.g. 16, 24, 32).
    /// </summary>
    public int BitsPerSample { get; init; }

    /// <summary>
    /// Formatted sample rate string (e.g. "48.0 kHz" or "44.1 kHz").
    /// </summary>
    public string FormattedSampleRate => SampleRate > 0 ? $"{SampleRate:N0} Hz" : "Sample rate unavailable";

    /// <summary>
    /// Formatted channel count string (e.g. "Stereo (2 ch)", "5.1 Surround (6 ch)").
    /// </summary>
    public string FormattedChannels => Channels switch
    {
        1 => "1 Channel (Mono)",
        2 => "2 Channels (Stereo)",
        4 => "4 Channels (Quadraphonic)",
        6 => "6 Channels (5.1 Surround)",
        8 => "8 Channels (7.1 Surround)",
        > 0 => $"{Channels} Channels",
        _ => "Channels unavailable"
    };

    /// <summary>
    /// Formatted audio specification string (e.g. "48.0 kHz, 24-bit, Stereo (2 ch)").
    /// </summary>
    public string FormattedAudioFormat
    {
        get
        {
            if (SampleRate <= 0 || Channels <= 0)
            {
                return "Format not available";
            }

            var channelText = Channels switch
            {
                1 => "Mono (1 ch)",
                2 => "Stereo (2 ch)",
                4 => "Quadraphonic (4 ch)",
                6 => "5.1 Surround (6 ch)",
                8 => "7.1 Surround (8 ch)",
                _ => $"{Channels} channels"
            };

            var bitText = BitsPerSample > 0 ? $"{BitsPerSample}-bit, " : string.Empty;
            return $"{SampleRate / 1000.0:0.#} kHz, {bitText}{channelText}";
        }
    }

    /// <summary>
    /// Quick status summary text.
    /// </summary>
    public string StateDescription => State switch
    {
        AudioDeviceState.Active => "Ready / Active",
        AudioDeviceState.Disabled => "Disabled in Windows",
        AudioDeviceState.NotPresent => "Hardware Not Present",
        AudioDeviceState.Unplugged => "Cable Unplugged",
        _ => State.ToString()
    };

    /// <summary>
    /// Whether the device is currently active and usable for audio output.
    /// </summary>
    public bool IsActive => State == AudioDeviceState.Active;
}

