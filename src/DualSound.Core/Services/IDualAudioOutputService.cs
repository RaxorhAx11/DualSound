using System;
using System.Threading;
using System.Threading.Tasks;
using DualSound.Contracts.Enums;
using DualSound.Contracts.Models;

namespace DualSound.Core.Services;

/// <summary>
/// Orchestrates simultaneous dual playback to two independent Windows audio endpoints
/// (TV via HDMI and Home Theatre via 3.5mm analog jack) with independent DSP and synchronization.
/// </summary>
public interface IDualAudioOutputService : IDisposable
{
    IAudioOutputBranchService TvBranch { get; }

    IAudioOutputBranchService HomeTheatreBranch { get; }

    AudioEngineState State { get; }

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Feeds captured canonical stereo float samples through the dual DSP and delay pipelines.
    /// </summary>
    void FeedCapturedAudio(ReadOnlySpan<float> canonicalStereoSamples);

    void ApplySettings(AppSettings settings);

    event EventHandler<AudioEngineState>? StateChanged;
    event EventHandler<string>? OutputError;
}

