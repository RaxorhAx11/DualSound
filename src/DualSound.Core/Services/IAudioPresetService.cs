using System;
using System.Collections.Generic;
using DualSound.Contracts.Enums;
using DualSound.Contracts.Models;

namespace DualSound.Core.Services;

/// <summary>
/// Manages standard and custom audio profile presets (Movie Theatre, Dialogue Boost, Bass Boost, Night Mode, Balanced, Custom).
/// </summary>
public interface IAudioPresetService
{
    IReadOnlyList<AudioPreset> AvailablePresets { get; }

    AudioPreset ActivePreset { get; }

    AudioPresetType ActivePresetType { get; }

    void ApplyPreset(AudioPresetType presetType);

    void SaveCustomPreset(OutputBranchSettings tvSettings, OutputBranchSettings htSettings);

    event EventHandler<AudioPreset>? PresetChanged;
}

