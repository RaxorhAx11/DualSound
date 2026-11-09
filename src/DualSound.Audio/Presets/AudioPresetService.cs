using System;
using System.Collections.Generic;
using System.Linq;
using DualSound.Contracts.Enums;
using DualSound.Contracts.Models;
using DualSound.Core.Services;
using Microsoft.Extensions.Logging;

namespace DualSound.Audio.Presets;

/// <summary>
/// Implements functional presets: Movie Theatre, Dialogue Boost, Bass Boost, Night Mode, Balanced, and Custom.
/// Updates DSP parameters and persists choices offline.
/// </summary>
public sealed class AudioPresetService : IAudioPresetService
{
    private readonly ISettingsService _settingsService;
    private readonly ILogger<AudioPresetService> _logger;
    private readonly List<AudioPreset> _presets = [];
    private AudioPreset _activePreset;

    public IReadOnlyList<AudioPreset> AvailablePresets => _presets;
    public AudioPreset ActivePreset => _activePreset;
    public AudioPresetType ActivePresetType => _activePreset.PresetType;

    public event EventHandler<AudioPreset>? PresetChanged;

    public AudioPresetService(ISettingsService settingsService, ILogger<AudioPresetService> logger)
    {
        _settingsService = settingsService;
        _logger = logger;

        InitializePresets();

        // Restore active preset from persistent settings
        var savedType = _settingsService.CurrentSettings.ActivePreset;
        _activePreset = _presets.FirstOrDefault(p => p.PresetType == savedType) ?? _presets[0];
    }

    public void ApplyPreset(AudioPresetType presetType)
    {
        var target = _presets.FirstOrDefault(p => p.PresetType == presetType);
        if (target == null) return;

        _activePreset = target;
        _settingsService.CurrentSettings.ActivePreset = presetType;

        // Copy preset values to active branch settings in AppSettings
        _settingsService.CurrentSettings.TvBranch.Equalizer = target.TvSettings.Equalizer.Clone();
        _settingsService.CurrentSettings.TvBranch.Dialogue = target.TvSettings.Dialogue.Clone();
        _settingsService.CurrentSettings.TvBranch.Bass = target.TvSettings.Bass.Clone();
        _settingsService.CurrentSettings.TvBranch.Safety = target.TvSettings.Safety.Clone();

        _settingsService.CurrentSettings.HomeTheatreBranch.Equalizer = target.HomeTheatreSettings.Equalizer.Clone();
        _settingsService.CurrentSettings.HomeTheatreBranch.Dialogue = target.HomeTheatreSettings.Dialogue.Clone();
        _settingsService.CurrentSettings.HomeTheatreBranch.Bass = target.HomeTheatreSettings.Bass.Clone();
        _settingsService.CurrentSettings.HomeTheatreBranch.Safety = target.HomeTheatreSettings.Safety.Clone();

        _ = _settingsService.SaveSettingsAsync(_settingsService.CurrentSettings);

        _logger.LogInformation("Audio preset applied: {PresetName} ({PresetType})", target.Name, target.PresetType);
        PresetChanged?.Invoke(this, _activePreset);
    }

    public void SaveCustomPreset(OutputBranchSettings tvSettings, OutputBranchSettings htSettings)
    {
        var custom = _presets.FirstOrDefault(p => p.PresetType == AudioPresetType.Custom);
        if (custom == null) return;

        custom.TvSettings = tvSettings.Clone();
        custom.HomeTheatreSettings = htSettings.Clone();

        _settingsService.CurrentSettings.CustomPreset = custom.Clone();
        _settingsService.CurrentSettings.ActivePreset = AudioPresetType.Custom;
        _activePreset = custom;

        _ = _settingsService.SaveSettingsAsync(_settingsService.CurrentSettings);

        _logger.LogInformation("Custom audio preset saved and applied");
        PresetChanged?.Invoke(this, _activePreset);
    }

    private void InitializePresets()
    {
        // 1. Movie Theatre
        var movie = new AudioPreset("Movie Theatre", AudioPresetType.MovieTheatre,
            "Optimized for cinema audio: TV provides standard natural soundtrack while Home Theatre provides enhanced dialogue and punchy bass.")
        {
            TvSettings = new OutputBranchSettings(AudioOutputBranch.TvHdmi)
            {
                Volume = 1.0f,
                Dialogue = { IsEnabled = false },
                Bass = { IsEnabled = false }
            },
            HomeTheatreSettings = new OutputBranchSettings(AudioOutputBranch.HomeTheatre35mm)
            {
                Volume = 1.0f,
                Dialogue = { IsEnabled = true, EnhancementAmount = 0.55f, VoiceBoostDb = 3.5f, CompressionRatio = 2.0f },
                Bass = { IsEnabled = true, BassGainDb = 5.5f, CrossoverFrequency = 110f, MonoSubwooferMix = true }
            }
        };
        // Boost 60 Hz and 120 Hz slightly on Home Theatre
        movie.HomeTheatreSettings.Equalizer.Bands[0].GainDb = 4f; // 60 Hz
        movie.HomeTheatreSettings.Equalizer.Bands[1].GainDb = 3f; // 120 Hz
        movie.HomeTheatreSettings.Equalizer.Bands[4].GainDb = 1.5f; // 1 kHz
        movie.HomeTheatreSettings.Equalizer.Bands[5].GainDb = 2.5f; // 2 kHz
        _presets.Add(movie);

        // 2. Dialogue Boost
        var dialogue = new AudioPreset("Dialogue Boost", AudioPresetType.DialogueBoost,
            "Maximum vocal clarity: Aggressive speech enhancement and leveling on Home Theatre, TV background sound gently tamed.")
        {
            TvSettings = new OutputBranchSettings(AudioOutputBranch.TvHdmi)
            {
                Volume = 0.85f,
                Dialogue = { IsEnabled = false },
                Bass = { IsEnabled = false }
            },
            HomeTheatreSettings = new OutputBranchSettings(AudioOutputBranch.HomeTheatre35mm)
            {
                Volume = 1.0f,
                Dialogue = { IsEnabled = true, EnhancementAmount = 0.85f, VoiceBoostDb = 6.5f, CompressionRatio = 2.8f },
                Bass = { IsEnabled = true, BassGainDb = 1.0f, CrossoverFrequency = 90f, MonoSubwooferMix = true }
            }
        };
        dialogue.HomeTheatreSettings.Equalizer.Bands[4].GainDb = 3.0f; // 1 kHz
        dialogue.HomeTheatreSettings.Equalizer.Bands[5].GainDb = 5.0f; // 2 kHz
        dialogue.HomeTheatreSettings.Equalizer.Bands[6].GainDb = 3.5f; // 4 kHz
        _presets.Add(dialogue);

        // 3. Bass Boost
        var bass = new AudioPreset("Bass Boost", AudioPresetType.BassBoost,
            "Deep cinematic impact: Home Theatre subwoofer delivers heavy low-frequency rumble with mono bass summing.")
        {
            TvSettings = new OutputBranchSettings(AudioOutputBranch.TvHdmi)
            {
                Volume = 0.95f,
                Dialogue = { IsEnabled = false },
                Bass = { IsEnabled = false }
            },
            HomeTheatreSettings = new OutputBranchSettings(AudioOutputBranch.HomeTheatre35mm)
            {
                Volume = 1.0f,
                Dialogue = { IsEnabled = true, EnhancementAmount = 0.35f, VoiceBoostDb = 2.0f, CompressionRatio = 1.8f },
                Bass = { IsEnabled = true, BassGainDb = 8.5f, CrossoverFrequency = 135f, MonoSubwooferMix = true }
            }
        };
        bass.HomeTheatreSettings.Equalizer.Bands[0].GainDb = 7.0f; // 60 Hz
        bass.HomeTheatreSettings.Equalizer.Bands[1].GainDb = 6.0f; // 120 Hz
        bass.HomeTheatreSettings.Equalizer.Bands[2].GainDb = 3.0f; // 250 Hz
        _presets.Add(bass);

        // 4. Night Mode
        var night = new AudioPreset("Night Mode", AudioPresetType.NightMode,
            "Late night viewing: Attenuates sub-bass and clamps dynamic range so dialogue is easily audible without loud peaks disturbing others.")
        {
            TvSettings = new OutputBranchSettings(AudioOutputBranch.TvHdmi)
            {
                Volume = 0.70f,
                Dialogue = { IsEnabled = false },
                Bass = { IsEnabled = false }
            },
            HomeTheatreSettings = new OutputBranchSettings(AudioOutputBranch.HomeTheatre35mm)
            {
                Volume = 0.85f,
                Dialogue = { IsEnabled = true, EnhancementAmount = 0.75f, VoiceBoostDb = 4.5f, CompressionRatio = 3.2f },
                Bass = { IsEnabled = true, BassGainDb = 0f, CrossoverFrequency = 80f, MonoSubwooferMix = false }
            }
        };
        night.HomeTheatreSettings.Equalizer.Bands[0].GainDb = -6.0f; // 60 Hz cut
        night.HomeTheatreSettings.Equalizer.Bands[1].GainDb = -4.0f; // 120 Hz cut
        night.TvSettings.Equalizer.Bands[0].GainDb = -6.0f;
        night.TvSettings.Equalizer.Bands[1].GainDb = -4.0f;
        _presets.Add(night);

        // 5. Balanced
        var balanced = new AudioPreset("Balanced", AudioPresetType.Balanced,
            "Reference neutral response: Flat 0 dB EQ across all bands, natural stereo imaging, uncolored frequency distribution.")
        {
            TvSettings = new OutputBranchSettings(AudioOutputBranch.TvHdmi)
            {
                Volume = 1.0f,
                Dialogue = { IsEnabled = false },
                Bass = { IsEnabled = false }
            },
            HomeTheatreSettings = new OutputBranchSettings(AudioOutputBranch.HomeTheatre35mm)
            {
                Volume = 1.0f,
                Dialogue = { IsEnabled = false },
                Bass = { IsEnabled = false }
            }
        };
        _presets.Add(balanced);

        // 6. Custom
        var savedCustom = _settingsService.CurrentSettings.CustomPreset;
        var custom = savedCustom ?? new AudioPreset("Custom", AudioPresetType.Custom,
            "User-configured profile: Saves custom 9-band EQ, dialogue parameters, bass crossover, and branch gains.")
        {
            TvSettings = new OutputBranchSettings(AudioOutputBranch.TvHdmi),
            HomeTheatreSettings = new OutputBranchSettings(AudioOutputBranch.HomeTheatre35mm)
        };
        _presets.Add(custom);

        _activePreset = movie;
    }
}

