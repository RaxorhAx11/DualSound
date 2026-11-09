using DualSound.Contracts.Enums;

namespace DualSound.Contracts.Models;

/// <summary>
/// Configuration for an independent physical output branch (e.g. TV HDMI or Home Theatre 3.5mm).
/// </summary>
public sealed class OutputBranchSettings
{
    public AudioOutputBranch Branch { get; set; }

    /// <summary>
    /// Target Windows audio playback endpoint ID.
    /// </summary>
    public string? DeviceId { get; set; }

    /// <summary>
    /// Friendly device name for display and logging.
    /// </summary>
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>
    /// Branch output enable toggle.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Output volume linear scale from 0.0f (silence) to 1.0f (full volume). Default 1.0f.
    /// </summary>
    public float Volume { get; set; } = 1.0f;

    /// <summary>
    /// Mute toggle.
    /// </summary>
    public bool IsMuted { get; set; } = false;

    /// <summary>
    /// Software synchronization delay in milliseconds (0 ms to 500 ms).
    /// </summary>
    public float DelayMilliseconds { get; set; } = 0f;

    /// <summary>
    /// 9-band graphic equalizer settings.
    /// </summary>
    public EqualizerSettings Equalizer { get; set; } = new();

    /// <summary>
    /// Dialogue enhancement settings (primary focus for Home Theatre branch).
    /// </summary>
    public DialogueEnhancementSettings Dialogue { get; set; } = new();

    /// <summary>
    /// Bass processing & crossover settings (primary focus for Home Theatre branch).
    /// </summary>
    public BassProcessingSettings Bass { get; set; } = new();

    /// <summary>
    /// Safety peak limiter and gain protection settings.
    /// </summary>
    public AudioSafetySettings Safety { get; set; } = new();

    public OutputBranchSettings() { }

    public OutputBranchSettings(AudioOutputBranch branch)
    {
        Branch = branch;
    }

    public OutputBranchSettings Clone() => new(Branch)
    {
        DeviceId = DeviceId,
        DeviceName = DeviceName,
        IsEnabled = IsEnabled,
        Volume = Volume,
        IsMuted = IsMuted,
        DelayMilliseconds = DelayMilliseconds,
        Equalizer = Equalizer.Clone(),
        Dialogue = Dialogue.Clone(),
        Bass = Bass.Clone(),
        Safety = Safety.Clone()
    };
}

