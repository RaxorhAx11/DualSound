using System;
using DualSound.Audio.Capture;
using DualSound.Contracts.Enums;
using DualSound.Contracts.Models;

namespace DualSound.Audio.Dsp;

/// <summary>
/// Modular real-time DSP pipeline for an individual audio output branch (TV HDMI or Home Theatre 3.5mm).
/// Executes Equalizer, Dialogue Clarity, Bass Processing, Volume/Mute scaling, and Peak Limiting in-place.
/// </summary>
public sealed class DspPipeline
{
    private readonly AudioOutputBranch _branch;
    private readonly float _sampleRate;

    private readonly NineBandEqualizer _equalizer;
    private readonly DialogueEnhancer _dialogueEnhancer;
    private readonly BassProcessor _bassProcessor;
    private readonly PeakLimiter _peakLimiter;
    private readonly AudioLevelMeter _levelMeter = new();

    private readonly object _settingsLock = new();
    private float _volume = 1.0f;
    private bool _isMuted = false;
    private bool _isEnabled = true;

    public AudioOutputBranch Branch => _branch;
    public NineBandEqualizer Equalizer => _equalizer;
    public DialogueEnhancer Dialogue => _dialogueEnhancer;
    public BassProcessor Bass => _bassProcessor;
    public PeakLimiter Limiter => _peakLimiter;
    public AudioLevels CurrentLevels => _levelMeter.CurrentLevels;

    public float Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0f, 1f);
    }

    public bool IsMuted
    {
        get => _isMuted;
        set => _isMuted = value;
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => _isEnabled = value;
    }

    public DspPipeline(AudioOutputBranch branch, float sampleRate = 48000f)
    {
        _branch = branch;
        _sampleRate = sampleRate;

        _equalizer = new NineBandEqualizer(_sampleRate);
        _dialogueEnhancer = new DialogueEnhancer(_sampleRate);
        _bassProcessor = new BassProcessor(_sampleRate);
        _peakLimiter = new PeakLimiter(_sampleRate);

        // Configure default branch behaviors:
        // TV branch defaults to normal audio (dialogue and bass bypass)
        // Home Theatre branch enables dialogue and bass processing
        if (branch == AudioOutputBranch.TvHdmi)
        {
            _dialogueEnhancer.IsEnabled = false;
            _bassProcessor.IsEnabled = false;
        }
        else
        {
            _dialogueEnhancer.IsEnabled = true;
            _bassProcessor.IsEnabled = true;
        }
    }

    /// <summary>
    /// Applies a branch settings configuration snapshot.
    /// </summary>
    public void ApplySettings(OutputBranchSettings settings)
    {
        lock (_settingsLock)
        {
            _isEnabled = settings.IsEnabled;
            _volume = Math.Clamp(settings.Volume, 0f, 1f);
            _isMuted = settings.IsMuted;

            _equalizer.ApplySettings(settings.Equalizer);
            _dialogueEnhancer.ApplySettings(settings.Dialogue);
            _bassProcessor.ApplySettings(settings.Bass);
            _peakLimiter.ApplySettings(settings.Safety);
        }
    }

    /// <summary>
    /// Clears internal state filters and level meters.
    /// </summary>
    public void Reset()
    {
        lock (_settingsLock)
        {
            _equalizer.Reset();
            _dialogueEnhancer.Reset();
            _bassProcessor.Reset();
            _peakLimiter.Reset();
            _levelMeter.Reset();
        }
    }

    /// <summary>
    /// Executes the full DSP chain on the provided stereo float buffer in-place.
    /// </summary>
    public void Process(Span<float> stereoSamples)
    {
        if (stereoSamples.IsEmpty) return;

        // 1. If branch is disabled or muted, zero out samples
        if (!_isEnabled || _isMuted)
        {
            stereoSamples.Clear();
            _levelMeter.ProcessSamples(stereoSamples);
            return;
        }

        lock (_settingsLock)
        {
            // 2. Equalizer stage
            _equalizer.Process(stereoSamples);

            // 3. Dialogue enhancement stage (applied primarily on Home Theatre branch)
            _dialogueEnhancer.Process(stereoSamples);

            // 4. Bass processing stage (applied primarily on Home Theatre branch)
            _bassProcessor.Process(stereoSamples);

            // 5. Volume scaling
            float vol = _volume;
            if (MathF.Abs(vol - 1.0f) > 0.001f)
            {
                for (int i = 0; i < stereoSamples.Length; i++)
                {
                    stereoSamples[i] *= vol;
                }
            }

            // 6. Audio safety limiter & clipping protection
            _peakLimiter.Process(stereoSamples);

            // 7. Post-DSP audio level metering for studio VU display
            _levelMeter.ProcessSamples(stereoSamples);
        }
    }
}

