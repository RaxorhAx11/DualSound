using System;
using System.Threading;
using System.Threading.Tasks;
using DualSound.Audio.Capture;
using DualSound.Contracts.Enums;
using DualSound.Contracts.Events;
using DualSound.Contracts.Models;
using DualSound.Core.Services;
using Microsoft.Extensions.Logging;

namespace DualSound.Audio.Engine;

/// <summary>
/// Master production implementation of the DualSound Audio Engine.
/// Coordinates real-time WASAPI capture, audio format conversion, branch DSP processing,
/// bounded millisecond delay synchronization, and simultaneous dual playback (HDMI TV + 3.5mm Home Theatre).
/// </summary>
public sealed class AudioEngineService : IAudioEngineService
{
    private readonly IAudioCapture _captureEngine;
    private readonly IDualAudioOutputService _outputService;
    private readonly IAudioSyncManager _syncManager;
    private readonly IAudioPresetService _presetService;
    private readonly IAudioDeviceMonitorService _monitorService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<AudioEngineService> _logger;
    private readonly object _stateLock = new();

    private AudioEngineState _state = AudioEngineState.Stopped;
    private bool _isDisposed = false;

    public AudioEngineState State
    {
        get
        {
            lock (_stateLock) return _state;
        }
    }

    public IDualAudioOutputService OutputService => _outputService;
    public IAudioSyncManager SyncManager => _syncManager;
    public IAudioPresetService PresetService => _presetService;

    public event EventHandler<AudioEngineState>? StateChanged;
    public event EventHandler<string>? EngineError;

    public AudioEngineService(
        IAudioCapture captureEngine,
        IDualAudioOutputService outputService,
        IAudioSyncManager syncManager,
        IAudioPresetService presetService,
        IAudioDeviceMonitorService monitorService,
        ISettingsService settingsService,
        ILogger<AudioEngineService> logger)
    {
        _captureEngine = captureEngine;
        _outputService = outputService;
        _syncManager = syncManager;
        _presetService = presetService;
        _monitorService = monitorService;
        _settingsService = settingsService;
        _logger = logger;

        // Wire real-time capture packet delivery to dual output feed
        _captureEngine.AudioPacketReceived += OnCapturePacketReceived;
        _captureEngine.CaptureError += OnCaptureError;

        // Wire preset changes to DSP pipeline
        _presetService.PresetChanged += OnPresetChanged;

        // Wire Windows device hotplug monitor
        _monitorService.DeviceChanged += OnDeviceLifecycleChanged;

        // Apply initial persisted settings
        _outputService.ApplySettings(_settingsService.CurrentSettings);
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(AudioEngineService));
            if (_state is AudioEngineState.Running or AudioEngineState.Initializing) return;

            SetState(AudioEngineState.Initializing);
        }

        try
        {
            _logger.LogInformation("Starting DualSound master audio engine...");

            // 1. Sync settings to output branches
            _outputService.ApplySettings(_settingsService.CurrentSettings);

            // 2. Start dual output playback clients
            await _outputService.StartAsync(cancellationToken);

            // 3. Start WASAPI capture client
            var captureDeviceId = _settingsService.CurrentSettings.PreferredCaptureDeviceId;
            await _captureEngine.StartAsync(captureDeviceId, cancellationToken);

            SetState(AudioEngineState.Running);
            _logger.LogInformation("DualSound master audio engine running successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start master audio engine");
            await StopInternalAsync();
            SetState(AudioEngineState.Error);
            EngineError?.Invoke(this, $"Engine start failed: {ex.Message}");
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (_state is AudioEngineState.Stopped) return;
        }

        _logger.LogInformation("Stopping DualSound master audio engine...");
        await StopInternalAsync(cancellationToken);

        lock (_stateLock)
        {
            SetState(AudioEngineState.Stopped);
        }

        _logger.LogInformation("DualSound master audio engine stopped");
    }

    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Restarting DualSound master audio engine...");
        await StopAsync(cancellationToken);
        await Task.Delay(250, cancellationToken);
        await StartAsync(cancellationToken);
    }

    private async Task StopInternalAsync(CancellationToken cancellationToken = default)
    {
        try { await _captureEngine.StopAsync(cancellationToken); } catch { }
        try { await _outputService.StopAsync(cancellationToken); } catch { }
        _syncManager.Reset();
    }

    private void OnCapturePacketReceived(object? sender, AudioPacketEventArgs e)
    {
        if (State != AudioEngineState.Running) return;

        // Deliver captured canonical samples to the dual output pipeline
        _outputService.FeedCapturedAudio(e.Samples.Span);
    }

    private void OnCaptureError(object? sender, AudioCaptureErrorEventArgs e)
    {
        _logger.LogWarning("Capture engine error reported: {Message}", e.Message);
        if (!e.IsRecoverable)
        {
            SetState(AudioEngineState.Error);
            EngineError?.Invoke(this, $"Capture Error: {e.Message}");
        }
    }

    private void OnPresetChanged(object? sender, AudioPreset preset)
    {
        _logger.LogInformation("Applying preset {PresetName} to active audio pipeline", preset.Name);
        _outputService.ApplySettings(_settingsService.CurrentSettings);
    }

    private void OnDeviceLifecycleChanged(object? sender, DeviceChangeEventArgs e)
    {
        _logger.LogInformation("Device change detected by audio engine: {ChangeType} on {DeviceId}", e.ChangeType, e.DeviceId);

        if (_settingsService.CurrentSettings.AutoRestartOnDeviceLoss && State == AudioEngineState.Running)
        {
            // If the changed device matches our capture or output endpoints, trigger seamless recovery
            bool isOurDevice = e.DeviceId == _settingsService.CurrentSettings.PreferredPrimaryDeviceId ||
                               e.DeviceId == _settingsService.CurrentSettings.PreferredSecondaryDeviceId ||
                               e.DeviceId == _settingsService.CurrentSettings.PreferredCaptureDeviceId;

            if (isOurDevice)
            {
                _logger.LogInformation("Active audio endpoint affected by device change. Initiating auto-recovery...");
                _ = Task.Run(async () =>
                {
                    try
                    {
                        SetState(AudioEngineState.Recovering);
                        await Task.Delay(1000); // Allow Windows audio driver to settle
                        await RestartAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Auto-recovery failed after device change");
                    }
                });
            }
        }
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
                _captureEngine.AudioPacketReceived -= OnCapturePacketReceived;
                _captureEngine.CaptureError -= OnCaptureError;
                _presetService.PresetChanged -= OnPresetChanged;
                _monitorService.DeviceChanged -= OnDeviceLifecycleChanged;

                try { _captureEngine.Dispose(); } catch { }
                try { _outputService.Dispose(); } catch { }
                try { _syncManager.Dispose(); } catch { }
            }
        }
    }
}
