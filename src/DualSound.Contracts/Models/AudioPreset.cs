using DualSound.Contracts.Enums;

namespace DualSound.Contracts.Models;

/// <summary>
/// Predefined or custom DSP audio profile preset.
/// </summary>
public sealed class AudioPreset
{
    public string Name { get; set; } = string.Empty;

    public AudioPresetType PresetType { get; set; }

    public string Description { get; set; } = string.Empty;

    public OutputBranchSettings TvSettings { get; set; } = new(AudioOutputBranch.TvHdmi);

    public OutputBranchSettings HomeTheatreSettings { get; set; } = new(AudioOutputBranch.HomeTheatre35mm);

    public AudioPreset() { }

    public AudioPreset(string name, AudioPresetType type, string description)
    {
        Name = name;
        PresetType = type;
        Description = description;
    }

    public AudioPreset Clone() => new()
    {
        Name = Name,
        PresetType = PresetType,
        Description = Description,
        TvSettings = TvSettings.Clone(),
        HomeTheatreSettings = HomeTheatreSettings.Clone()
    };
}

