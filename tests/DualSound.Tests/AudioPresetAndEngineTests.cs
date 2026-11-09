using System;
using System.Linq;
using DualSound.Audio.Presets;
using DualSound.Contracts.Enums;
using DualSound.Contracts.Models;
using DualSound.Core.Services;
using DualSound.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DualSound.Tests;

public class AudioPresetAndEngineTests
{
    [Fact]
    public void AudioPresetService_LoadsBuiltInPresetsCorrectly()
    {
        var settingsService = new JsonSettingsService(NullLogger<JsonSettingsService>.Instance);
        var presetService = new AudioPresetService(settingsService, NullLogger<AudioPresetService>.Instance);

        var presets = presetService.AvailablePresets;

        Assert.NotEmpty(presets);
        Assert.Contains(presets, p => p.PresetType == AudioPresetType.MovieTheatre);
        Assert.Contains(presets, p => p.PresetType == AudioPresetType.DialogueBoost);
        Assert.Contains(presets, p => p.PresetType == AudioPresetType.BassBoost);
        Assert.Contains(presets, p => p.PresetType == AudioPresetType.NightMode);
        Assert.Contains(presets, p => p.PresetType == AudioPresetType.Balanced);
    }

    [Fact]
    public void AudioPresetService_DialogueBoost_ConfiguresEnhancedSpeech()
    {
        var settingsService = new JsonSettingsService(NullLogger<JsonSettingsService>.Instance);
        var presetService = new AudioPresetService(settingsService, NullLogger<AudioPresetService>.Instance);

        var preset = presetService.AvailablePresets.First(p => p.PresetType == AudioPresetType.DialogueBoost);

        Assert.NotNull(preset);
        Assert.True(preset.HomeTheatreSettings.Dialogue.IsEnabled);
        Assert.True(preset.HomeTheatreSettings.Dialogue.VoiceBoostDb >= 4.0f);
    }

    [Fact]
    public void AudioPresetService_BassBoost_ConfiguresLowPassAndBoost()
    {
        var settingsService = new JsonSettingsService(NullLogger<JsonSettingsService>.Instance);
        var presetService = new AudioPresetService(settingsService, NullLogger<AudioPresetService>.Instance);

        var preset = presetService.AvailablePresets.First(p => p.PresetType == AudioPresetType.BassBoost);

        Assert.NotNull(preset);
        Assert.True(preset.HomeTheatreSettings.Bass.IsEnabled);
        Assert.True(preset.HomeTheatreSettings.Bass.BassGainDb >= 4.0f);
        Assert.False(preset.HomeTheatreSettings.Bass.LowPassFilterEnabled);
    }

    [Fact]
    public void AudioPresetService_NightMode_EnablesSafetyLimiter()
    {
        var settingsService = new JsonSettingsService(NullLogger<JsonSettingsService>.Instance);
        var presetService = new AudioPresetService(settingsService, NullLogger<AudioPresetService>.Instance);

        var preset = presetService.AvailablePresets.First(p => p.PresetType == AudioPresetType.NightMode);

        Assert.NotNull(preset);
        Assert.True(preset.TvSettings.Safety.PeakLimiterEnabled);
        Assert.True(preset.HomeTheatreSettings.Safety.PeakLimiterEnabled);
    }

    [Fact]
    public void AudioPresetService_ApplyPreset_UpdatesBranchSettings()
    {
        var settingsService = new JsonSettingsService(NullLogger<JsonSettingsService>.Instance);
        var presetService = new AudioPresetService(settingsService, NullLogger<AudioPresetService>.Instance);

        presetService.ApplyPreset(AudioPresetType.BassBoost);

        Assert.Equal(AudioPresetType.BassBoost, presetService.ActivePreset.PresetType);
        Assert.True(settingsService.CurrentSettings.HomeTheatreBranch.Bass.BassGainDb > 0);
    }
}
