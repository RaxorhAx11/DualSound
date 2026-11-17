namespace DualSound.Contracts.Models;

/// <summary>
/// Represents a paired virtual audio driver configuration (e.g. VB-Audio Virtual Cable).
/// Pairs the Windows render endpoint (CABLE Input) with the corresponding capture endpoint (CABLE Output).
/// </summary>
public sealed class VirtualAudioCablePair
{
    /// <summary>
    /// Virtual render / playback endpoint (e.g. "CABLE Input (VB-Audio Virtual Cable)").
    /// Windows applications and media players route their playback to this device.
    /// </summary>
    public AudioDeviceInfo? PlaybackDevice { get; set; }

    /// <summary>
    /// Virtual capture / recording endpoint (e.g. "CABLE Output (VB-Audio Virtual Cable)").
    /// DualSound captures desktop audio from this device.
    /// </summary>
    public AudioDeviceInfo? RecordingDevice { get; set; }

    /// <summary>
    /// Virtual driver brand or model description (e.g. "VB-Audio Virtual Cable").
    /// </summary>
    public string DriverName { get; set; } = "Virtual Audio Cable";

    /// <summary>
    /// Whether both endpoints are present and active in the Windows audio subsystem.
    /// </summary>
    public bool IsInstalled => PlaybackDevice != null && RecordingDevice != null;

    /// <summary>
    /// Whether the virtual playback endpoint is currently the Windows default playback device.
    /// </summary>
    public bool IsDefaultPlayback => PlaybackDevice?.IsDefaultPlayback == true;

    /// <summary>
    /// Whether the virtual recording endpoint is assigned as DualSound's capture source.
    /// </summary>
    public bool IsCaptureAssigned { get; set; }
}

