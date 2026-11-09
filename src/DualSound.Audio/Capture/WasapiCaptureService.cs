using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DualSound.Contracts.Enums;
using DualSound.Contracts.Events;
using DualSound.Contracts.Models;
using DualSound.Core.Services;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;

#pragma warning disable CS0618 // WasapiCapture is standard across NAudio 3.x; WasapiRecorderBuilder is experimental preview

namespace DualSound.Audio.Capture;

/// <summary>
/// Production-grade WASAPI audio capture service.
/// Captures audio from any selected Windows recording endpoint (e.g. VB-Audio CABLE Output or Microphone),
/// converts all input sample formats to canonical 48 kHz float stereo, and tracks buffer health and VU levels.
/// </summary>
public sealed class WasapiCaptureService : IAudioCapture
{
    private readonly IAudioDeviceDiscoveryService _discoveryService;
    private readonly IAudioDeviceMonitorService _monitorService;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<WasapiCaptureService> _logger;

    private readonly object _stateLock = new();
    private readonly AudioFormatConverter _formatConverter = new();
    private readonly AudioLevelMeter _levelMeter = new();
    private readonly BoundedAudioBuffer _buffer;

    private WasapiCapture? _capture;
    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _currentMmDevice;
    private Timer? _meterTimer;

    private AudioCaptureState _state = AudioCaptureState.Stopped;
    private AudioCaptureOptions _options;
    private AudioDeviceInfo? _selectedDevice;

    private long _totalFramesCaptured;
    private int _restartAttempts;
    private bool _isDisposed;

    // Source format cache
    private int _sourceSampleRate;
    private int _sourceChannels;
    private int _sourceBitsPerSample;
    private string _sourceFormatDescription = "None";

    // Reusable event packet buffer to eliminate allocations in real-time capture callback
    private float[] _eventPacketBuffer = new float[8192];

    /// <inheritdoc />
    public AudioCaptureState State
    {
        get
        {
            lock (_stateLock)
            {
                return _state;
            }
        }
    }

    /// <inheritdoc />
    public AudioCaptureOptions Options => _options;

    /// <inheritdoc />
    public AudioDeviceInfo? SelectedDevice
    {
        get
        {
            lock (_stateLock)
            {
                return _selectedDevice;
            }
        }
    }

    /// <inheritdoc />
    public AudioLevels CurrentLevels => _levelMeter.CurrentLevels;

    /// <inheritdoc />
    public BoundedAudioBuffer Buffer => _buffer;

    /// <inheritdoc />
    public long TotalFramesCaptured => Interlocked.Read(ref _totalFramesCaptured);

    /// <inheritdoc />
    public long BufferOverflows => _buffer.OverflowCount;

    /// <inheritdoc />
    public float BufferOccupancyPercentage => _buffer.OccupancyPercentage;

    /// <inheritdoc />
    public int SourceSampleRate => _sourceSampleRate;

    /// <inheritdoc />
    public int SourceChannels => _sourceChannels;

    /// <inheritdoc />
    public string SourceFormatDescription => _sourceFormatDescription;

    /// <inheritdoc />
    public event EventHandler<AudioCaptureStateChangedEventArgs>? StateChanged;

    /// <inheritdoc />
    public event EventHandler<AudioLevelsEventArgs>? LevelsUpdated;

    /// <inheritdoc />
    public event EventHandler<AudioPacketEventArgs>? AudioPacketReceived;

    /// <inheritdoc />
    public event EventHandler<AudioCaptureErrorEventArgs>? CaptureError;

    public WasapiCaptureService(
        IAudioDeviceDiscoveryService discoveryService,
        IAudioDeviceMonitorService monitorService,
        ISettingsService settingsService,
        ILogger<WasapiCaptureService> logger,
        AudioCaptureOptions? options = null)
    {
        _discoveryService = discoveryService;
        _monitorService = monitorService;
        _settingsService = settingsService;
        _logger = logger;
        _options = options ?? new AudioCaptureOptions();

        // 1.0s capacity of 48kHz stereo float = 96,000 samples
        int bufferSamples = (int)(_options.TargetSampleRate * _options.TargetChannels * _options.RingBufferDurationSeconds);
        _buffer = new BoundedAudioBuffer(bufferSamples);

        _monitorService.DeviceChanged += OnDeviceLifecycleChanged;

        // Start metering timer at ~30 Hz (33ms)
        _meterTimer = new Timer(OnMeterTimerTick, null, 33, 33);
    }

    /// <inheritdoc />
    public void SetDevice(AudioDeviceInfo? device)
    {
        lock (_stateLock)
        {
            _selectedDevice = device;
            if (device != null)
            {
                _settingsService.CurrentSettings.PreferredCaptureDeviceId = device.Id;
                _ = _settingsService.SaveSettingsAsync(_settingsService.CurrentSettings);
            }
        }
    }

    /// <inheritdoc />
    public async Task StartAsync(string? deviceId = null, CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(WasapiCaptureService));
            }

            if (_state is AudioCaptureState.Capturing or AudioCaptureState.Starting)
            {
                return;
            }

            SetState(AudioCaptureState.Starting, "Initializing WASAPI capture client...");
        }

        try
        {
            await Task.Run(() => InitializeAndStartCapture(deviceId), cancellationToken);
            _restartAttempts = 0;
            SetState(AudioCaptureState.Capturing, "Capturing real-time audio stream");
            _logger.LogInformation("WASAPI audio capture engine started on device {DeviceName} ({Format})",
                _selectedDevice?.Name ?? "Default", _sourceFormatDescription);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start WASAPI audio capture");
            CleanupCaptureResources();
            SetState(AudioCaptureState.Error, $"Capture start failed: {ex.Message}");
            CaptureError?.Invoke(this, new AudioCaptureErrorEventArgs($"Failed to start capture: {ex.Message}", ex, isRecoverable: true));
            throw;
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (_state is AudioCaptureState.Stopped or AudioCaptureState.Stopping)
            {
                return;
            }

            SetState(AudioCaptureState.Stopping, "Stopping capture engine...");
        }

        await Task.Run(CleanupCaptureResources, cancellationToken);

        _buffer.Clear();
        _formatConverter.Reset();
        _levelMeter.Reset();

        SetState(AudioCaptureState.Stopped, "Capture engine stopped");
        _logger.LogInformation("WASAPI audio capture stopped cleanly");
    }

    private void InitializeAndStartCapture(string? deviceId)
    {
        CleanupCaptureResources();

        _enumerator = new MMDeviceEnumerator();

        // 1. Resolve target recording device
        MMDevice? targetDevice = null;
        var targetId = deviceId ?? _selectedDevice?.Id ?? _settingsService.CurrentSettings.PreferredCaptureDeviceId;

        if (!string.IsNullOrWhiteSpace(targetId))
        {
            try
            {
                targetDevice = _enumerator.GetDevice(targetId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Specified capture device ID {DeviceId} could not be opened, falling back to default", targetId);
            }
        }

        if (targetDevice == null)
        {
            try
            {
                targetDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia) ??
                               _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("No Windows audio recording endpoint is available.", ex);
            }
        }

        if (targetDevice == null)
        {
            throw new InvalidOperationException("No suitable Windows audio recording endpoint could be found.");
        }

        _currentMmDevice = targetDevice;

        // Update selected device contract record
        _selectedDevice = AudioDeviceMapper.MapToDeviceInfo(
            _currentMmDevice,
            defaultPlaybackId: null,
            defaultCommunicationsId: null,
            defaultRecordingId: targetDevice.ID,
            dataFlow: AudioDataFlow.Capture,
            logger: _logger);

        // 2. Initialize WasapiCapture without event-sync lockups
        _capture = new WasapiCapture(_currentMmDevice, useEventSync: false, _options.BufferDurationMilliseconds);

        var waveFormat = _capture.WaveFormat;
        _sourceSampleRate = waveFormat.SampleRate;
        _sourceChannels = waveFormat.Channels;
        _sourceBitsPerSample = waveFormat.BitsPerSample;
        _sourceFormatDescription = $"{_sourceSampleRate:N0} Hz, {_sourceBitsPerSample}-bit, {(_sourceChannels == 1 ? "Mono" : $"{_sourceChannels} Ch")}";

        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;

        _buffer.Clear();
        _formatConverter.Reset();
        _levelMeter.Reset();
        Interlocked.Exchange(ref _totalFramesCaptured, 0);

        // 3. Start recording
        _capture.StartRecording();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0 || _state != AudioCaptureState.Capturing)
        {
            return;
        }

        try
        {
            var format = _capture?.WaveFormat;
            if (format == null)
            {
                return;
            }

            // Allocation-conscious conversion to 48kHz float stereo
            var canonicalSamples = _formatConverter.ConvertToCanonicalStereo(e.Buffer, e.BytesRecorded, format);
            if (canonicalSamples.IsEmpty)
            {
                return;
            }

            // Write to bounded ring buffer (handles overflow automatically)
            _buffer.Write(canonicalSamples);

            // Update real-time audio levels
            _levelMeter.ProcessSamples(canonicalSamples);

            Interlocked.Add(ref _totalFramesCaptured, canonicalSamples.Length / 2);

            // Dispatch packet event if subscribers are listening (allocation-free using reusable event buffer)
            if (AudioPacketReceived != null)
            {
                if (_eventPacketBuffer.Length < canonicalSamples.Length)
                {
                    _eventPacketBuffer = new float[canonicalSamples.Length * 2];
                }
                canonicalSamples.CopyTo(_eventPacketBuffer.AsSpan(0, canonicalSamples.Length));

                AudioPacketReceived.Invoke(this, new AudioPacketEventArgs(
                    _eventPacketBuffer.AsMemory(0, canonicalSamples.Length),
                    canonicalSamples.Length / 2,
                    _options.TargetSampleRate,
                    _options.TargetChannels));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in real-time WASAPI audio callback");
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            _logger.LogError(e.Exception, "WASAPI capture stopped unexpectedly with error");
            HandleCaptureFailure(e.Exception);
        }
        else
        {
            _logger.LogInformation("WASAPI recording stopped successfully");
            lock (_stateLock)
            {
                if (_state == AudioCaptureState.Stopping)
                {
                    SetState(AudioCaptureState.Stopped, "Capture stopped");
                }
            }
        }
    }

    private void HandleCaptureFailure(Exception exception)
    {
        bool isDeviceLost = exception is COMException comEx &&
                           ((uint)comEx.HResult == 0x88890004 || // AUDCLNT_E_DEVICE_INVALIDATED
                            (uint)comEx.HResult == 0x88890001 || // AUDCLNT_E_NOT_INITIALIZED
                            (uint)comEx.HResult == 0x80070490);  // ERROR_NOT_FOUND

        lock (_stateLock)
        {
            if (_state is AudioCaptureState.Stopped or AudioCaptureState.Stopping)
            {
                return;
            }

            if (_options.AutoRestartOnDeviceLoss && _restartAttempts < _options.MaxRestartAttempts)
            {
                _restartAttempts++;
                SetState(AudioCaptureState.Interrupted, $"Capture interrupted (attempt {_restartAttempts}/{_options.MaxRestartAttempts}). Reconnecting...");
                _logger.LogWarning("Capture interrupted, scheduling auto-restart attempt {Attempt}/{Max}",
                    _restartAttempts, _options.MaxRestartAttempts);

                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(1 << _restartAttempts, 5)));
                    try
                    {
                        await StartAsync(_selectedDevice?.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Restart attempt {Attempt} failed", _restartAttempts);
                    }
                });
                return;
            }

            SetState(AudioCaptureState.Error, $"Capture halted: {exception.Message}");
            CaptureError?.Invoke(this, new AudioCaptureErrorEventArgs(exception.Message, exception, isRecoverable: false));
        }
    }

    private void OnDeviceLifecycleChanged(object? sender, DeviceChangeEventArgs e)
    {
        if (_state != AudioCaptureState.Capturing || _selectedDevice == null)
        {
            return;
        }

        if (string.Equals(_selectedDevice.Id, e.DeviceId, StringComparison.OrdinalIgnoreCase))
        {
            if (e.ChangeType == DeviceChangeType.DeviceRemoved ||
                (e.ChangeType == DeviceChangeType.DeviceStateChanged && e.NewState != AudioDeviceState.Active))
            {
                _logger.LogWarning("Active capture device {DeviceId} state changed to {State}", e.DeviceId, e.NewState);
                HandleCaptureFailure(new InvalidOperationException($"Recording device {_selectedDevice.Name} was disconnected."));
            }
        }
    }

    private void OnMeterTimerTick(object? state)
    {
        if (_state == AudioCaptureState.Capturing)
        {
            LevelsUpdated?.Invoke(this, new AudioLevelsEventArgs(_levelMeter.CurrentLevels));
        }
        else
        {
            _levelMeter.DecayLevels();
            LevelsUpdated?.Invoke(this, new AudioLevelsEventArgs(_levelMeter.CurrentLevels));
        }
    }

    private void CleanupCaptureResources()
    {
        if (_capture != null)
        {
            try
            {
                _capture.DataAvailable -= OnDataAvailable;
                _capture.RecordingStopped -= OnRecordingStopped;
                _capture.StopRecording();
            }
            catch { }

            try
            {
                _capture.Dispose();
            }
            catch { }

            _capture = null;
        }

        if (_currentMmDevice != null)
        {
            try { _currentMmDevice.Dispose(); } catch { }
            _currentMmDevice = null;
        }

        if (_enumerator != null)
        {
            try { _enumerator.Dispose(); } catch { }
            _enumerator = null;
        }
    }

    private void SetState(AudioCaptureState newState, string? message = null)
    {
        AudioCaptureState oldState;
        lock (_stateLock)
        {
            oldState = _state;
            _state = newState;
        }

        if (oldState != newState)
        {
            StateChanged?.Invoke(this, new AudioCaptureStateChangedEventArgs(oldState, newState, message));
        }
    }

    public void Dispose()
    {
        lock (_stateLock)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _monitorService.DeviceChanged -= OnDeviceLifecycleChanged;
        }

        _meterTimer?.Dispose();
        _meterTimer = null;

        CleanupCaptureResources();
        GC.SuppressFinalize(this);
    }
}
