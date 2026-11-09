namespace DualSound.Contracts.Enums;

/// <summary>
/// Hardware connection category of an audio device endpoint, identified via Windows device properties.
/// </summary>
public enum AudioDeviceType
{
    /// <summary>
    /// Connection type could not be reliably determined from hardware properties.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// HDMI or DisplayPort digital audio display device (e.g. Connected TV or monitor).
    /// </summary>
    HdmiDisplayPort = 1,

    /// <summary>
    /// 3.5 mm analog audio connection (Headphones, Line-Out, or Front/Rear 3.5mm jack).
    /// </summary>
    LineOut35mm = 2,

    /// <summary>
    /// Built-in or external analog speakers.
    /// </summary>
    Speakers = 3,

    /// <summary>
    /// Analog or wired headphones endpoint.
    /// </summary>
    Headphones = 4,

    /// <summary>
    /// External USB audio interface, USB DAC, or USB headset.
    /// </summary>
    UsbAudio = 5,

    /// <summary>
    /// Wireless Bluetooth audio device (A2DP / HFP / LE Audio).
    /// </summary>
    BluetoothAudio = 6,

    /// <summary>
    /// Virtual / Software audio endpoint (e.g. VB-Audio Cable, SteelSeries Sonar, Voicemeeter).
    /// </summary>
    VirtualAudio = 7
}

