using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DualSound.Contracts.Enums;
using DualSound.Contracts.Models;
using DualSound.Core.Services;
using DualSound.Core.ViewModels;
using Microsoft.Extensions.Logging;

namespace DualSound.App.ViewModels;

/// <summary>
/// Production ViewModel for 9-band digital biquad Equalizer, Dialogue Enhancement, and Bass Processing.
/// Supports independent acoustic shaping for TV HDMI (ambient/natural) and Home Theatre 3.5mm (vocal clarity + sub punch).
/// </summary>
public sealed partial class EqualizerViewModel : ViewModelBase
{
    private readonly IAudioEngineService _audioEngine;
    private readonly IAudioPresetService _presetService;
    private readonly ISettingsService _settingsService;
    private readonly IDispatcherService _dispatcherService;
    private readonly ILogger<EqualizerViewModel> _logger;

    private bool _isUpdatingInternally = false;

    // Active output branch selection: 0 = TV (HDMI), 1 = Home Theatre (3.5 mm)
    [ObservableProperty]
    private int _selectedBranchIndex = 1; // Default to Home Theatre

    [ObservableProperty]
    private bool _isHomeTheatreSelected = true;

    [ObservableProperty]
    private string _branchHeader = "Home Theatre / 3.5 mm Acoustic Processing";

    // 9-Band Graphic EQ Gains (-12 dB to +12 dB)
    [ObservableProperty] private float _gain60Hz;
    [ObservableProperty] private float _gain120Hz;
    [ObservableProperty] private float _gain250Hz;
    [ObservableProperty] private float _gain500Hz;
    [ObservableProperty] private float _gain1000Hz;
    [ObservableProperty] private float _gain2000Hz;
    [ObservableProperty] private float _gain4000Hz;
    [ObservableProperty] private float _gain8000Hz;
    [ObservableProperty] private float _gain16000Hz;

    [ObservableProperty]
    private bool _isEqEnabled = true;

    // Dialogue Enhancement (Home Theatre Branch)
    [ObservableProperty]
    private bool _isDialogueEnabled = true;

    [ObservableProperty]
    private float _dialogueEnhancementAmount = 0.55f;

    [ObservableProperty]
    private float _dialogueVoiceBoostDb = 3.5f;

    [ObservableProperty]
    private float _dialogueClarityFrequency = 2500f;

    [ObservableProperty]
    private float _dialogueCompressionRatio = 2.0f;

    // Bass Processing & Crossover (Home Theatre Branch)
    [ObservableProperty]
    private bool _isBassEnabled = true;

    [ObservableProperty]
    private float _bassGainDb = 4.0f;

    [ObservableProperty]
    private float _bassCrossoverFrequency = 120f;

    [ObservableProperty]
    private bool _isLowPassFilterEnabled = true;

    [ObservableProperty]
    private bool _isMonoSubwooferMix = true;

    // Preset & Notification State
    [ObservableProperty]
    private AudioPresetType _activePresetType;

    [ObservableProperty]
    private string _activePresetName = string.Empty;

    [ObservableProperty]
    private string? _statusNotification;

    public IReadOnlyList<AudioPreset> Presets => _presetService.AvailablePresets;

    public EqualizerViewModel(
        IAudioEngineService audioEngine,
        IAudioPresetService presetService,
        ISettingsService settingsService,
        IDispatcherService dispatcherService,
        ILogger<EqualizerViewModel> logger)
    {
        _audioEngine = audioEngine;
        _presetService = presetService;
        _settingsService = settingsService;
        _dispatcherService = dispatcherService;
        _logger = logger;

        Title = "Equalizer";

        _activePresetType = _presetService.ActivePresetType;
        _activePresetName = _presetService.ActivePreset.Name;

        _presetService.PresetChanged += OnPresetChanged;

        LoadBranchSettings(AudioOutputBranch.HomeTheatre35mm);
    }

    [RelayCommand]
    public void SwitchBranch(string branchIndex)
    {
        if (int.TryParse(branchIndex, out int idx))
        {
            SelectedBranchIndex = idx;
        }
    }

    partial void OnSelectedBranchIndexChanged(int value)
    {
        IsHomeTheatreSelected = value == 1;
        BranchHeader = IsHomeTheatreSelected
            ? "Home Theatre / 3.5 mm Acoustic Processing"
            : "TV / HDMI Output Equalizer";

        var branch = IsHomeTheatreSelected ? AudioOutputBranch.HomeTheatre35mm : AudioOutputBranch.TvHdmi;
        LoadBranchSettings(branch);
    }

    private void LoadBranchSettings(AudioOutputBranch branch)
    {
        _isUpdatingInternally = true;
        try
        {
            var branchSettings = branch == AudioOutputBranch.HomeTheatre35mm
                ? _settingsService.CurrentSettings.HomeTheatreBranch
                : _settingsService.CurrentSettings.TvBranch;

            IsEqEnabled = branchSettings.Equalizer.IsEnabled;
            if (branchSettings.Equalizer.Bands.Count >= 9)
            {
                Gain60Hz = branchSettings.Equalizer.Bands[0].GainDb;
                Gain120Hz = branchSettings.Equalizer.Bands[1].GainDb;
                Gain250Hz = branchSettings.Equalizer.Bands[2].GainDb;
                Gain500Hz = branchSettings.Equalizer.Bands[3].GainDb;
                Gain1000Hz = branchSettings.Equalizer.Bands[4].GainDb;
                Gain2000Hz = branchSettings.Equalizer.Bands[5].GainDb;
                Gain4000Hz = branchSettings.Equalizer.Bands[6].GainDb;
                Gain8000Hz = branchSettings.Equalizer.Bands[7].GainDb;
                Gain16000Hz = branchSettings.Equalizer.Bands[8].GainDb;
            }

            // Dialogue settings
            IsDialogueEnabled = branchSettings.Dialogue.IsEnabled;
            DialogueEnhancementAmount = branchSettings.Dialogue.EnhancementAmount;
            DialogueVoiceBoostDb = branchSettings.Dialogue.VoiceBoostDb;
            DialogueClarityFrequency = branchSettings.Dialogue.ClarityFrequency;
            DialogueCompressionRatio = branchSettings.Dialogue.CompressionRatio;

            // Bass settings
            IsBassEnabled = branchSettings.Bass.IsEnabled;
            BassGainDb = branchSettings.Bass.BassGainDb;
            BassCrossoverFrequency = branchSettings.Bass.CrossoverFrequency;
            IsLowPassFilterEnabled = branchSettings.Bass.LowPassFilterEnabled;
            IsMonoSubwooferMix = branchSettings.Bass.MonoSubwooferMix;
        }
        finally
        {
            _isUpdatingInternally = false;
        }
    }

    private void PushActiveSettingsToPipeline()
    {
        if (_isUpdatingInternally) return;

        var branch = IsHomeTheatreSelected ? AudioOutputBranch.HomeTheatre35mm : AudioOutputBranch.TvHdmi;
        var branchSettings = branch == AudioOutputBranch.HomeTheatre35mm
            ? _settingsService.CurrentSettings.HomeTheatreBranch
            : _settingsService.CurrentSettings.TvBranch;

        // Update Equalizer
        branchSettings.Equalizer.IsEnabled = IsEqEnabled;
        if (branchSettings.Equalizer.Bands.Count >= 9)
        {
            branchSettings.Equalizer.Bands[0].GainDb = Gain60Hz;
            branchSettings.Equalizer.Bands[1].GainDb = Gain120Hz;
            branchSettings.Equalizer.Bands[2].GainDb = Gain250Hz;
            branchSettings.Equalizer.Bands[3].GainDb = Gain500Hz;
            branchSettings.Equalizer.Bands[4].GainDb = Gain1000Hz;
            branchSettings.Equalizer.Bands[5].GainDb = Gain2000Hz;
            branchSettings.Equalizer.Bands[6].GainDb = Gain4000Hz;
            branchSettings.Equalizer.Bands[7].GainDb = Gain8000Hz;
            branchSettings.Equalizer.Bands[8].GainDb = Gain16000Hz;
        }

        // Update Dialogue
        branchSettings.Dialogue.IsEnabled = IsDialogueEnabled;
        branchSettings.Dialogue.EnhancementAmount = DialogueEnhancementAmount;
        branchSettings.Dialogue.VoiceBoostDb = DialogueVoiceBoostDb;
        branchSettings.Dialogue.ClarityFrequency = DialogueClarityFrequency;
        branchSettings.Dialogue.CompressionRatio = DialogueCompressionRatio;

        // Update Bass
        branchSettings.Bass.IsEnabled = IsBassEnabled;
        branchSettings.Bass.BassGainDb = BassGainDb;
        branchSettings.Bass.CrossoverFrequency = BassCrossoverFrequency;
        branchSettings.Bass.LowPassFilterEnabled = IsLowPassFilterEnabled;
        branchSettings.Bass.MonoSubwooferMix = IsMonoSubwooferMix;

        // Apply immediately to real-time DSP pipeline
        _audioEngine.OutputService.ApplySettings(_settingsService.CurrentSettings);
        _ = _settingsService.SaveSettingsAsync(_settingsService.CurrentSettings);
    }

    // Property changed handlers triggering real-time DSP update
    partial void OnGain60HzChanged(float value) => PushActiveSettingsToPipeline();
    partial void OnGain120HzChanged(float value) => PushActiveSettingsToPipeline();
    partial void OnGain250HzChanged(float value) => PushActiveSettingsToPipeline();
    partial void OnGain500HzChanged(float value) => PushActiveSettingsToPipeline();
    partial void OnGain1000HzChanged(float value) => PushActiveSettingsToPipeline();
    partial void OnGain2000HzChanged(float value) => PushActiveSettingsToPipeline();
    partial void OnGain4000HzChanged(float value) => PushActiveSettingsToPipeline();
    partial void OnGain8000HzChanged(float value) => PushActiveSettingsToPipeline();
    partial void OnGain16000HzChanged(float value) => PushActiveSettingsToPipeline();
    partial void OnIsEqEnabledChanged(bool value) => PushActiveSettingsToPipeline();

    partial void OnIsDialogueEnabledChanged(bool value) => PushActiveSettingsToPipeline();
    partial void OnDialogueEnhancementAmountChanged(float value) => PushActiveSettingsToPipeline();
    partial void OnDialogueVoiceBoostDbChanged(float value) => PushActiveSettingsToPipeline();
    partial void OnDialogueClarityFrequencyChanged(float value) => PushActiveSettingsToPipeline();
    partial void OnDialogueCompressionRatioChanged(float value) => PushActiveSettingsToPipeline();

    partial void OnIsBassEnabledChanged(bool value) => PushActiveSettingsToPipeline();
    partial void OnBassGainDbChanged(float value) => PushActiveSettingsToPipeline();
    partial void OnBassCrossoverFrequencyChanged(float value) => PushActiveSettingsToPipeline();
    partial void OnIsLowPassFilterEnabledChanged(bool value) => PushActiveSettingsToPipeline();
    partial void OnIsMonoSubwooferMixChanged(bool value) => PushActiveSettingsToPipeline();

    [RelayCommand]
    public void SelectPreset(AudioPresetType type)
    {
        _presetService.ApplyPreset(type);
        StatusNotification = $"Preset applied: {_presetService.ActivePreset.Name}";
    }

    [RelayCommand]
    public void ResetEq()
    {
        _isUpdatingInternally = true;
        try
        {
            Gain60Hz = 0f;
            Gain120Hz = 0f;
            Gain250Hz = 0f;
            Gain500Hz = 0f;
            Gain1000Hz = 0f;
            Gain2000Hz = 0f;
            Gain4000Hz = 0f;
            Gain8000Hz = 0f;
            Gain16000Hz = 0f;
        }
        finally
        {
            _isUpdatingInternally = false;
        }
        PushActiveSettingsToPipeline();
        StatusNotification = "Equalizer reset to flat 0 dB response";
    }

    [RelayCommand]
    public void SaveAsCustomPreset()
    {
        _presetService.SaveCustomPreset(_settingsService.CurrentSettings.TvBranch, _settingsService.CurrentSettings.HomeTheatreBranch);
        StatusNotification = "Current settings saved to Custom Preset";
    }

    private void OnPresetChanged(object? sender, AudioPreset preset)
    {
        _dispatcherService.Invoke(() =>
        {
            ActivePresetType = preset.PresetType;
            ActivePresetName = preset.Name;
            var branch = IsHomeTheatreSelected ? AudioOutputBranch.HomeTheatre35mm : AudioOutputBranch.TvHdmi;
            LoadBranchSettings(branch);
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _presetService.PresetChanged -= OnPresetChanged;
        }
        base.Dispose(disposing);
    }
}
