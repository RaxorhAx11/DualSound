namespace DualSound.Contracts.Models;

/// <summary>
/// Settings for real-time dialogue clarity enhancement.
/// Emphasizes speech intelligibility band (1.5 kHz - 3.5 kHz) with gentle dynamic leveling.
/// </summary>
public sealed class DialogueEnhancementSettings
{
    /// <summary>
    /// Whether dialogue enhancement processor is enabled on this branch.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Master dialogue enhancement mix factor from 0.0 (bypass) to 1.0 (full effect).
    /// </summary>
    public float EnhancementAmount { get; set; } = 0.5f;

    /// <summary>
    /// Speech band gain boost in decibels (0 to +9 dB). Default is +3.5 dB.
    /// </summary>
    public float VoiceBoostDb { get; set; } = 3.5f;

    /// <summary>
    /// Center frequency for dialogue clarity peaking filter in Hertz. Default is 2500 Hz.
    /// </summary>
    public float ClarityFrequency { get; set; } = 2500f;

    /// <summary>
    /// Soft-knee compression ratio for evening out quiet whispered dialogue and loud explosions.
    /// Values typically 1.5:1 to 3.0:1. Default is 2.0:1.
    /// </summary>
    public float CompressionRatio { get; set; } = 2.0f;

    public DialogueEnhancementSettings Clone() => new()
    {
        IsEnabled = IsEnabled,
        EnhancementAmount = EnhancementAmount,
        VoiceBoostDb = VoiceBoostDb,
        ClarityFrequency = ClarityFrequency,
        CompressionRatio = CompressionRatio
    };
}

