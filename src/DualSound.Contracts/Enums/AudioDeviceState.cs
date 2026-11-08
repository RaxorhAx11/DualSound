namespace DualSound.Contracts.Enums;

/// <summary>
/// Represents the operational state of a Windows audio device endpoint.
/// Directly maps to Windows CoreAudio MMDevice DeviceState flags.
/// </summary>
public enum AudioDeviceState
{
    /// <summary>
    /// The audio device is active and available for playback or capture.
    /// </summary>
    Active = 1,

    /// <summary>
    /// The audio device is disabled by the user or system administrator.
    /// </summary>
    Disabled = 2,

    /// <summary>
    /// The audio device is not present (e.g. unplugged USB/Bluetooth hardware or detached adapter).
    /// </summary>
    NotPresent = 4,

    /// <summary>
    /// The audio endpoint is present on the system but the cable or connector is unplugged (e.g. 3.5mm jack).
    /// </summary>
    Unplugged = 8,

    /// <summary>
    /// All states combined for filtering or querying.
    /// </summary>
    All = Active | Disabled | NotPresent | Unplugged
}
