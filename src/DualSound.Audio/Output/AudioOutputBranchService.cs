using System;
using DualSound.Audio.Dsp;
using DualSound.Audio.Sync;
using DualSound.Contracts.Enums;
using DualSound.Contracts.Models;
using DualSound.Core.Services;
using Microsoft.Extensions.Logging;

namespace DualSound.Audio.Output;

/// <summary>
/// Service encapsulating an individual audio output branch (TV HDMI or Home Theatre 3.5mm),
/// coordinating its DSP pipeline, bounded delay line, and WASAPI playback client.
/// </summary>
public sealed class AudioOutputBranchService : IAudioOutputBranchService, IDisposable
{
    private readonly AudioOutputBranch _branch;
    private readonly DspPipeline _dsp;
    private readonly OutputDelayBuffer _delayBuffer;
    private readonly WasapiPlaybackClient _client;
    private readonly ILogger _logger;

    public AudioOutputBranch Branch => _branch;
    public DspPipeline Dsp => _dsp;
    public OutputDelayBuffer DelayBuffer => _delayBuffer;
    public WasapiPlaybackClient Client => _client;

    public string? DeviceId => _client.DeviceId;
    public string DeviceName => _client.DeviceName;
    public string FormatDescription => _client.FormatDescription;
    public AudioEngineState State => _client.State;
    public AudioLevels CurrentLevels => _dsp.CurrentLevels;
    public AudioSyncStatus SyncStatus => _delayBuffer.GetStatus();

    public float Volume
    {
        get => _dsp.Volume;
        set => _dsp.Volume = value;
    }

    public bool IsMuted
    {
        get => _dsp.IsMuted;
        set => _dsp.IsMuted = value;
    }

    public bool IsEnabled
    {
        get => _dsp.IsEnabled;
        set => _dsp.IsEnabled = value;
    }

    public float DelayMilliseconds
    {
        get => _delayBuffer.TargetDelayMs;
        set => _delayBuffer.SetDelayMilliseconds(value);
    }

    public AudioOutputBranchService(
        AudioOutputBranch branch,
        OutputDelayBuffer delayBuffer,
        ILogger logger,
        float sampleRate = 48000f)
    {
        _branch = branch;
        _delayBuffer = delayBuffer;
        _logger = logger;

        _dsp = new DspPipeline(branch, sampleRate);
        _client = new WasapiPlaybackClient(branch, delayBuffer, logger);
    }

    public void SetDevice(string? deviceId)
    {
        _client.SetDevice(deviceId);
    }

    public void ApplySettings(OutputBranchSettings settings)
    {
        _client.SetDevice(settings.DeviceId);
        _dsp.ApplySettings(settings);
        _delayBuffer.SetDelayMilliseconds(settings.DelayMilliseconds);
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}

