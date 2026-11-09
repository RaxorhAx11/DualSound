using System;
using System.Threading;
using System.Threading.Tasks;
using DualSound.Contracts.Enums;

namespace DualSound.Core.Services;

/// <summary>
/// Master coordinator for the entire DualSound offline audio pipeline.
/// Synchronizes capture, dual output branches, DSP filters, delay lines, presets, and device failovers.
/// </summary>
public interface IAudioEngineService : IDisposable
{
    AudioEngineState State { get; }

    IDualAudioOutputService OutputService { get; }

    IAudioSyncManager SyncManager { get; }

    IAudioPresetService PresetService { get; }

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task RestartAsync(CancellationToken cancellationToken = default);

    event EventHandler<AudioEngineState>? StateChanged;

    event EventHandler<string>? EngineError;
}

