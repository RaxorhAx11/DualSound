using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DualSound.Audio.Sync;
using DualSound.Contracts.Enums;
using DualSound.Contracts.Models;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

#pragma warning disable CS0618

namespace DualSound.Audio.Output;

/// <summary>
/// Independent WASAPI playback client for an individual physical output branch (TV HDMI or Home Theatre 3.5mm).
/// Negotiates native hardware MixFormats, performs real-time resampling if target device clock differs from 48 kHz,
/// provides isolated error containment, and handles automatic device reconnection.
/// </summary>
public sealed class WasapiPlaybackClient : IDisposable
{
    private readonly AudioOutputBranch _branch;
    private readonly OutputDelayBuffer _delayBuffer;
    private readonly ILogger _logger;
    private readonly object _stateLock = new();

    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _currentMmDevice;
    private WasapiOut? _wasapiOut;
    private BranchPlaybackSampleProvider? _sampleProvider;

    private string? _deviceId;
    private string _deviceName = "None";
    private string _formatDescription = "Not Initialized";
    private AudioEngineState _state = AudioEngineState.Stopped;
    private bool _isDisposed = false;
    private int _reconnectAttempts = 0;

    public AudioOutputBranch Branch => _branch;
    public AudioEngineState State
    {
        get
        {
            lock (_stateLock) return _state;
        }
    }
    public string? DeviceId => _deviceId;
    public string DeviceName => _deviceName;
    public string FormatDescription => _formatDescription;

    public event EventHandler<AudioEngineState>? StateChanged;
    public event EventHandler<string>? ErrorOccurred;

    public WasapiPlaybackClient(AudioOutputBranch branch, OutputDelayBuffer delayBuffer, ILogger logger)
    {
        _branch = branch;
        _delayBuffer = delayBuffer;
        _logger = logger;
    }

    /// <summary>
    /// Configures the target output device for this branch.
    /// </summary>
    public void SetDevice(string? deviceId)
    {
        lock (_stateLock)
        {
            _deviceId = deviceId;
        }
    }

    /// <summary>
    /// Initializes WASAPI playback client and starts rendering audio.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(WasapiPlaybackClient));
            if (_state is AudioEngineState.Running or AudioEngineState.Initializing) return;

            SetState(AudioEngineState.Initializing);
        }

        try
        {
            await Task.Run(InitializeAndStartPlayback, cancellationToken);
            _reconnectAttempts = 0;
            SetState(AudioEngineState.Running);
            _logger.LogInformation("WASAPI playback branch {Branch} started on device '{DeviceName}' ({Format})",
                _branch, _deviceName, _formatDescription);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start WASAPI playback branch {Branch}", _branch);
            CleanupResources();
            SetState(AudioEngineState.Error);
            ErrorOccurred?.Invoke(this, $"Failed to start {_branch}: {ex.Message}");
            // Non-fatal to other branch by design
        }
    }

    /// <summary>
    /// Stops playback and releases device client.
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (_state is AudioEngineState.Stopped) return;
        }

        await Task.Run(() =>
        {
            lock (_stateLock)
            {
                if (_sampleProvider != null)
                {
                    _sampleProvider.IsPlaying = false;
                }

                if (_wasapiOut != null)
                {
                    try { _wasapiOut.Stop(); } catch { }
                }

                CleanupResources();
                SetState(AudioEngineState.Stopped);
            }
        }, cancellationToken);

        _logger.LogInformation("WASAPI playback branch {Branch} stopped cleanly", _branch);
    }

    private void InitializeAndStartPlayback()
    {
        lock (_stateLock)
        {
            CleanupResources();

            _enumerator = new MMDeviceEnumerator();

            // Resolve target device
            if (!string.IsNullOrWhiteSpace(_deviceId))
            {
                try
                {
                    _currentMmDevice = _enumerator.GetDevice(_deviceId);
                }
                catch
                {
                    _currentMmDevice = null;
                }
            }

            if (_currentMmDevice == null)
            {
                // Fallback to default render device if preferred not found
                _currentMmDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            }

            if (_currentMmDevice == null)
            {
                throw new InvalidOperationException($"No playback audio endpoint available for {_branch}");
            }

            _deviceName = _currentMmDevice.FriendlyName;

            // Query native hardware mix format
            var mixFormat = _currentMmDevice.AudioClient.MixFormat;
            _formatDescription = $"{mixFormat.SampleRate} Hz, {mixFormat.Channels} Ch, {mixFormat.BitsPerSample}-bit ({mixFormat.Encoding})";

            // Create our 48 kHz stereo pull sample provider
            _sampleProvider = new BranchPlaybackSampleProvider(_delayBuffer, 48000);

            // Adapt sample provider to device mix format
            IWaveProvider waveProvider = AdaptToDeviceFormat(_sampleProvider, mixFormat);

            // Create WasapiOut in Shared mode (50ms latency target for stability)
            _wasapiOut = new WasapiOut(_currentMmDevice, AudioClientShareMode.Shared, false, 50);
            _wasapiOut.Init(waveProvider);
            _wasapiOut.PlaybackStopped += OnPlaybackStopped;

            _sampleProvider.IsPlaying = true;
            _wasapiOut.Play();
        }
    }

    private IWaveProvider AdaptToDeviceFormat(ISampleProvider sourceSampleProvider, WaveFormat targetMixFormat)
    {
        ISampleProvider pipeline = sourceSampleProvider;

        // 1. Resample if hardware endpoint does not use 48 kHz (e.g. 44.1 kHz, 96 kHz, 192 kHz)
        if (targetMixFormat.SampleRate != 48000)
        {
            _logger.LogInformation("Resampling branch {Branch} from 48000 Hz to hardware native {SampleRate} Hz",
                _branch, targetMixFormat.SampleRate);
            pipeline = new WdlResamplingSampleProvider(pipeline, targetMixFormat.SampleRate);
        }

        // 2. Channel adaptation (e.g. if target endpoint requires multi-channel 5.1/7.1)
        if (targetMixFormat.Channels > 2)
        {
            pipeline = new MultiChannelPanner(pipeline, targetMixFormat.Channels);
        }

        // 3. Bit depth / Encoding adaptation
        if (targetMixFormat.Encoding == WaveFormatEncoding.Pcm && targetMixFormat.BitsPerSample == 16)
        {
            return new SampleToWaveProvider16(pipeline);
        }

        // Default: IEEE float wave provider
        return new SampleToWaveProvider(pipeline);
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            _logger.LogWarning(e.Exception, "Playback stopped on branch {Branch} due to error", _branch);
            HandleDeviceFailure(e.Exception);
        }
        else
        {
            lock (_stateLock)
            {
                if (_state != AudioEngineState.Stopped)
                {
                    SetState(AudioEngineState.Stopped);
                }
            }
        }
    }

    private void HandleDeviceFailure(Exception exception)
    {
        bool isDeviceLost = exception is COMException comEx &&
                           ((uint)comEx.HResult == 0x88890004 || // AUDCLNT_E_DEVICE_INVALIDATED
                            (uint)comEx.HResult == 0x88890001 || // AUDCLNT_E_NOT_INITIALIZED
                            (uint)comEx.HResult == 0x80070490);  // ERROR_NOT_FOUND

        lock (_stateLock)
        {
            if (_state is AudioEngineState.Stopped) return;

            SetState(AudioEngineState.Recovering);
            CleanupResources();

            if (_reconnectAttempts < 5)
            {
                _reconnectAttempts++;
                int delaySec = Math.Min(1 << _reconnectAttempts, 5);
                _logger.LogWarning("Scheduling reconnection attempt {Attempt}/5 for branch {Branch} in {Delay}s",
                    _reconnectAttempts, _branch, delaySec);

                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(delaySec));
                    try
                    {
                        await StartAsync();
                    }
                    catch { }
                });
            }
            else
            {
                SetState(AudioEngineState.Error);
                ErrorOccurred?.Invoke(this, $"Device lost for branch {_branch} and max retries exceeded");
            }
        }
    }

    private void SetState(AudioEngineState newState)
    {
        _state = newState;
        StateChanged?.Invoke(this, newState);
    }

    private void CleanupResources()
    {
        if (_wasapiOut != null)
        {
            try
            {
                _wasapiOut.PlaybackStopped -= OnPlaybackStopped;
                _wasapiOut.Dispose();
            }
            catch { }
            _wasapiOut = null;
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

    public void Dispose()
    {
        lock (_stateLock)
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                CleanupResources();
            }
        }
    }

    /// <summary>
    /// Helper to expand stereo to multi-channel (e.g. 5.1/7.1) without crashing WASAPI shared mode.
    /// </summary>
    private sealed class MultiChannelPanner : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _targetChannels;
        private readonly WaveFormat _waveFormat;
        private readonly float[] _sourceBuffer;

        public WaveFormat WaveFormat => _waveFormat;

        public MultiChannelPanner(ISampleProvider source, int targetChannels)
        {
            _source = source;
            _targetChannels = targetChannels;
            _waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, targetChannels);
            _sourceBuffer = new float[2048 * source.WaveFormat.Channels];
        }

        public int Read(float[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(offset, count));
        }

        public int Read(Span<float> buffer)
        {
            int targetFrames = buffer.Length / _targetChannels;
            int sourceSamplesNeeded = targetFrames * _source.WaveFormat.Channels;

            float[] tempSource = _sourceBuffer.Length >= sourceSamplesNeeded ? _sourceBuffer : new float[sourceSamplesNeeded];
            int readSourceSamples = _source.Read(tempSource.AsSpan(0, sourceSamplesNeeded));
            int readFrames = readSourceSamples / _source.WaveFormat.Channels;

            buffer.Clear();

            for (int f = 0; f < readFrames; f++)
            {
                int srcIdx = f * _source.WaveFormat.Channels;
                int dstIdx = f * _targetChannels;

                float left = tempSource[srcIdx];
                float right = tempSource[srcIdx + 1];

                buffer[dstIdx] = left;          // Ch0: Left
                buffer[dstIdx + 1] = right;      // Ch1: Right

                // Ch2: Center, Ch3: LFE (subwoofer)
                if (_targetChannels >= 4)
                {
                    buffer[dstIdx + 2] = (left + right) * 0.5f; // Center
                    buffer[dstIdx + 3] = (left + right) * 0.5f; // LFE
                }
            }

            return readFrames * _targetChannels;
        }
    }
}
