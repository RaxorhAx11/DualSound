using System;
using System.Threading;
using System.Threading.Tasks;
using DualSound.Audio.Sync;
using DualSound.Contracts.Enums;
using DualSound.Contracts.Models;
using DualSound.Core.Services;
using Microsoft.Extensions.Logging;

namespace DualSound.Audio.Output;

/// <summary>
/// Core implementation of the simultaneous dual audio output distribution service.
/// Distributes captured audio to independent TV HDMI and Home Theatre 3.5mm streams
/// with branch-specific DSP, delay synchronization, and isolated hardware error boundaries.
/// </summary>
public sealed class DualAudioOutputService : IDualAudioOutputService
{
    private readonly AudioOutputBranchService _tvBranch;
    private readonly AudioOutputBranchService _htBranch;
    private readonly IAudioSyncManager _syncManager;
    private readonly ILogger<DualAudioOutputService> _logger;
    private readonly object _stateLock = new();

    private AudioEngineState _state = AudioEngineState.Stopped;
    private bool _isDisposed = false;

    // Preallocated scratch buffers for zero-allocation DSP processing
    private float[] _tvScratchBuffer = new float[8192];
    private float[] _htScratchBuffer = new float[8192];

    public IAudioOutputBranchService TvBranch => _tvBranch;
    public IAudioOutputBranchService HomeTheatreBranch => _htBranch;

    public AudioEngineState State
    {
        get
        {
            lock (_stateLock) return _state;
        }
    }

    public event EventHandler<AudioEngineState>? StateChanged;
    public event EventHandler<string>? OutputError;

    public DualAudioOutputService(
        IAudioSyncManager syncManager,
        ILogger<DualAudioOutputService> logger,
        ILogger<AudioOutputBranchService> branchLogger)
    {
        _syncManager = syncManager;
        _logger = logger;

        if (syncManager is AudioSyncManager concreteSync)
        {
            _tvBranch = new AudioOutputBranchService(AudioOutputBranch.TvHdmi, concreteSync.TvDelayBuffer, branchLogger);
            _htBranch = new AudioOutputBranchService(AudioOutputBranch.HomeTheatre35mm, concreteSync.HomeTheatreDelayBuffer, branchLogger);
        }
        else
        {
            var tvBuf = new OutputDelayBuffer(AudioOutputBranch.TvHdmi);
            var htBuf = new OutputDelayBuffer(AudioOutputBranch.HomeTheatre35mm);
            _tvBranch = new AudioOutputBranchService(AudioOutputBranch.TvHdmi, tvBuf, branchLogger);
            _htBranch = new AudioOutputBranchService(AudioOutputBranch.HomeTheatre35mm, htBuf, branchLogger);
        }

        _tvBranch.Client.ErrorOccurred += (s, msg) => OutputError?.Invoke(this, $"TV HDMI Error: {msg}");
        _htBranch.Client.ErrorOccurred += (s, msg) => OutputError?.Invoke(this, $"Home Theatre Error: {msg}");
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(DualAudioOutputService));
            if (_state is AudioEngineState.Running or AudioEngineState.Initializing) return;

            SetState(AudioEngineState.Initializing);
        }

        try
        {
            _logger.LogInformation("Starting simultaneous dual output streams...");

            // Start TV and Home Theatre playback branches concurrently
            var tvTask = _tvBranch.Client.StartAsync(cancellationToken);
            var htTask = _htBranch.Client.StartAsync(cancellationToken);

            await Task.WhenAll(tvTask, htTask);

            SetState(AudioEngineState.Running);
            _logger.LogInformation("Simultaneous dual audio playback running: TV ({TvDevice}) and Home Theatre ({HtDevice})",
                _tvBranch.DeviceName, _htBranch.DeviceName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start dual audio output branches");
            SetState(AudioEngineState.Error);
            OutputError?.Invoke(this, $"Failed to start outputs: {ex.Message}");
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (_state is AudioEngineState.Stopped) return;
        }

        _logger.LogInformation("Stopping dual output playback streams...");

        var tvStop = _tvBranch.Client.StopAsync(cancellationToken);
        var htStop = _htBranch.Client.StopAsync(cancellationToken);

        await Task.WhenAll(tvStop, htStop);

        lock (_stateLock)
        {
            SetState(AudioEngineState.Stopped);
        }

        _logger.LogInformation("Dual output playback streams stopped cleanly");
    }

    /// <summary>
    /// Feeds captured audio frames through both independent branch DSP and delay pipelines.
    /// Allocation-free in the hot real-time audio loop.
    /// </summary>
    public void FeedCapturedAudio(ReadOnlySpan<float> canonicalStereoSamples)
    {
        if (canonicalStereoSamples.IsEmpty) return;

        // Ensure scratch buffers are large enough
        int len = canonicalStereoSamples.Length;
        if (_tvScratchBuffer.Length < len)
        {
            Array.Resize(ref _tvScratchBuffer, len * 2);
            Array.Resize(ref _htScratchBuffer, len * 2);
        }

        // 1. Process TV Branch (HDMI)
        canonicalStereoSamples.CopyTo(_tvScratchBuffer.AsSpan(0, len));
        var tvSpan = _tvScratchBuffer.AsSpan(0, len);
        _tvBranch.Dsp.Process(tvSpan);
        _tvBranch.DelayBuffer.Write(tvSpan);

        // 2. Process Home Theatre Branch (3.5 mm)
        canonicalStereoSamples.CopyTo(_htScratchBuffer.AsSpan(0, len));
        var htSpan = _htScratchBuffer.AsSpan(0, len);
        _htBranch.Dsp.Process(htSpan);
        _htBranch.DelayBuffer.Write(htSpan);
    }

    public void ApplySettings(AppSettings settings)
    {
        _tvBranch.ApplySettings(settings.TvBranch);
        _htBranch.ApplySettings(settings.HomeTheatreBranch);
    }

    private void SetState(AudioEngineState newState)
    {
        _state = newState;
        StateChanged?.Invoke(this, newState);
    }

    public void Dispose()
    {
        lock (_stateLock)
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                _tvBranch.Dispose();
                _htBranch.Dispose();
            }
        }
    }
}

