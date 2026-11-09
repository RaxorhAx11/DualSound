namespace DualSound.Contracts.Enums;

/// <summary>
/// Audio data flow direction (rendering to speakers or capturing from recording devices).
/// </summary>
public enum AudioDataFlow
{
    /// <summary>
    /// Playback / Render endpoint (e.g. Speakers, HDMI TV, Headphones).
    /// </summary>
    Render = 0,

    /// <summary>
    /// Recording / Capture endpoint (e.g. Virtual Cable Output, Microphone, Line In).
    /// </summary>
    Capture = 1
}

