using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DualSound.Contracts.Enums;
using DualSound.Contracts.Events;
using DualSound.Contracts.Models;
using DualSound.Core.Services;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

#pragma warning disable CS0618

namespace DualSound.Audio.Routing;

/// <summary>
/// Production service managing system-wide Windows desktop audio routing verification,
/// virtual cable pairing, bypass condition diagnostics, safe test tone generation, and Sound Settings workflows.
/// </summary>
public sealed class SystemAudioRoutingService : ISystemAudioRoutingService
{
    private readonly IAudioDeviceDiscoveryService _discoveryService;
    private readonly IAudioDeviceMonitorService _monitorService;
    private readonly ISettingsService _settingsService;
    private readonly IDualAudioOutputService _outputService;
    private readonly ILogger<SystemAudioRoutingService> _logger;
    private readonly object _lock = new();

    private SystemAudioRoutingStatus _currentStatus = new();
    private VirtualAudioCablePair? _detectedVirtualCable;
    private CancellationTokenSource? _testToneCts;
    private bool _isPlayingTestTone;
    private bool _isDisposed;

    public SystemAudioRoutingStatus CurrentStatus
    {
        get
        {
            lock (_lock) return _currentStatus;
        }
    }

    public VirtualAudioCablePair? DetectedVirtualCable
    {
        get
        {
            lock (_lock) return _detectedVirtualCable;
        }
    }

    public bool IsPlayingTestTone
    {
        get
        {
            lock (_lock) return _isPlayingTestTone;
        }
    }

    public event EventHandler<SystemAudioRoutingStatus>? RoutingStatusChanged;

    public SystemAudioRoutingService(
        IAudioDeviceDiscoveryService discoveryService,
        IAudioDeviceMonitorService monitorService,
        ISettingsService settingsService,
        IDualAudioOutputService outputService,
        ILogger<SystemAudioRoutingService> logger)
    {
        _discoveryService = discoveryService;
        _monitorService = monitorService;
        _settingsService = settingsService;
        _outputService = outputService;
        _logger = logger;

        // Auto-refresh routing diagnostics whenever Windows device endpoints or default device changes
        _monitorService.DeviceChanged += OnAudioDeviceChanged;

        // Initial scan
        _ = ValidateRoutingAsync();
    }

    public async Task<SystemAudioRoutingStatus> ValidateRoutingAsync()
    {
        try
        {
            var playbackDevices = await _discoveryService.GetPlaybackDevicesAsync(includeInactive: false);
            var recordingDevices = await _discoveryService.GetRecordingDevicesAsync(includeInactive: false);
            var defaultPlayback = await _discoveryService.GetDefaultPlaybackDeviceAsync();

            var virtualPair = await DetectVirtualAudioCableAsync();
            var settings = _settingsService.CurrentSettings;

            var status = new SystemAudioRoutingStatus
            {
                WindowsDefaultPlaybackDevice = defaultPlayback,
                DetectedVirtualCable = virtualPair,
                VirtualPlaybackDevice = virtualPair?.PlaybackDevice,
                VirtualRecordingDevice = virtualPair?.RecordingDevice,
                IsEngineRunning = _outputService.State == AudioEngineState.Running
            };

            // Resolve physical outputs
            var tvId = settings.TvBranch?.DeviceId ?? settings.PreferredPrimaryDeviceId;
            var htId = settings.HomeTheatreBranch?.DeviceId ?? settings.PreferredSecondaryDeviceId;

            status.TvOutputDevice = playbackDevices.FirstOrDefault(d => d.Id == tvId);
            status.HomeTheatreOutputDevice = playbackDevices.FirstOrDefault(d => d.Id == htId);

            // Populate documented bypass conditions (Section 3 of audit)
            status.KnownBypasses.Add("Applications with custom output device assignments in Windows Settings will bypass DualSound.");
            status.KnownBypasses.Add("Applications using exclusive-mode streams (e.g. ASIO or WASAPI Exclusive) bypass the Windows mixer.");
            status.KnownBypasses.Add("Protected DRM media streams with loopback copy-protection may fail to route through virtual cables.");

            // Diagnostic Step 1: Check Virtual Audio Cable Installation
            if (virtualPair == null || !virtualPair.IsInstalled)
            {
                status.ValidationState = RoutingValidationState.VirtualCableMissing;
                status.StatusTitle = "Virtual Audio Cable Not Found";
                status.StatusSummary = "DualSound requires a virtual audio cable (e.g. VB-Audio Virtual Cable) to capture all desktop audio.";
                status.RecommendedActionText = "Install VB-Audio Virtual Cable (CABLE Input / CABLE Output) and restart DualSound.";
                status.ActionCommandOrUrl = "https://vb-audio.com/Cable/";
                UpdateStatus(status);
                return status;
            }

            // Diagnostic Step 2: Check Physical Output Selections
            if (status.TvOutputDevice == null || status.HomeTheatreOutputDevice == null)
            {
                status.ValidationState = RoutingValidationState.PhysicalOutputMissing;
                status.StatusTitle = "Physical Output Devices Unassigned or Disconnected";
                status.StatusSummary = "Both TV (HDMI) and Home Theatre (3.5 mm) outputs must be selected and active.";
                status.RecommendedActionText = "Configure both output devices in Audio Routing or launch the Setup Wizard.";
                UpdateStatus(status);
                return status;
            }

            // Diagnostic Step 3: Check for Conflicting Identical Output Selection
            if (string.Equals(status.TvOutputDevice.Id, status.HomeTheatreOutputDevice.Id, StringComparison.OrdinalIgnoreCase))
            {
                status.ValidationState = RoutingValidationState.OutputsConflict;
                status.StatusTitle = "Conflicting Output Assignment";
                status.StatusSummary = "The TV and Home Theatre outputs are assigned to the exact same audio device.";
                status.RecommendedActionText = "Select two different physical audio devices for TV and Home Theatre.";
                UpdateStatus(status);
                return status;
            }

            status.IsTvConfiguredCorrectly = true;
            status.IsHomeTheatreConfiguredCorrectly = true;

            // Diagnostic Step 4: Verify Capture Endpoint Configuration
            var captureId = settings.PreferredCaptureDeviceId;
            var matchedCapture = recordingDevices.FirstOrDefault(d => d.Id == captureId) ??
                                 recordingDevices.FirstOrDefault(d => d.Id == virtualPair.RecordingDevice?.Id);

            if (matchedCapture == null || !matchedCapture.IsActive)
            {
                status.ValidationState = RoutingValidationState.CaptureAccessDenied;
                status.StatusTitle = "Virtual Recording Source Unavailable";
                status.StatusSummary = "The virtual recording endpoint (CABLE Output) is disabled or inaccessible.";
                status.RecommendedActionText = "Enable CABLE Output in Windows Sound Control Panel.";
                status.ActionCommandOrUrl = "ms-settings:sound";
                UpdateStatus(status);
                return status;
            }

            status.IsCaptureConfiguredCorrectly = true;

            // Diagnostic Step 5: Verify Windows System Default Playback Endpoint Routing
            bool isRoutedToVirtualCable = defaultPlayback != null &&
                                          virtualPair.PlaybackDevice != null &&
                                          (string.Equals(defaultPlayback.Id, virtualPair.PlaybackDevice.Id, StringComparison.OrdinalIgnoreCase) ||
                                           defaultPlayback.Name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase));

            status.IsDesktopAudioRoutedToVirtualCable = isRoutedToVirtualCable;

            if (!isRoutedToVirtualCable)
            {
                status.ValidationState = RoutingValidationState.DefaultDeviceMismatch;
                status.StatusTitle = "Desktop Audio Not Routed to Virtual Cable";
                status.StatusSummary = $"Windows default playback is currently set to '{defaultPlayback?.Name ?? "None"}'. Desktop audio is bypassing DualSound.";
                status.RecommendedActionText = $"Open Windows Sound Settings and set default output to '{virtualPair.PlaybackDevice?.Name ?? "CABLE Input"}'.";
                status.ActionCommandOrUrl = "ms-settings:sound";
                UpdateStatus(status);
                return status;
            }

            // All diagnostic checks passed
            status.ValidationState = RoutingValidationState.FullyConfiguredAndReady;
            status.StatusTitle = "System Audio Routing: Ready";
            status.StatusSummary = $"All desktop audio playing to '{virtualPair.PlaybackDevice?.Name ?? "Virtual Cable"}' is routed to DualSound and distributed to '{status.TvOutputDevice?.Name ?? "TV"}' and '{status.HomeTheatreOutputDevice?.Name ?? "Home Theatre"}'.";
            status.RecommendedActionText = "Configuration verified. Start the audio engine to begin multi-output distribution.";

            UpdateStatus(status);
            return status;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run audio routing diagnostic");
            var errorStatus = new SystemAudioRoutingStatus
            {
                ValidationState = RoutingValidationState.Uninitialized,
                StatusTitle = "Diagnostic Error",
                StatusSummary = $"Could not evaluate Windows audio routing: {ex.Message}",
                RecommendedActionText = "Check Windows Audio service status."
            };
            UpdateStatus(errorStatus);
            return errorStatus;
        }
    }

    public async Task<VirtualAudioCablePair?> DetectVirtualAudioCableAsync()
    {
        var playbacks = await _discoveryService.GetPlaybackDevicesAsync(includeInactive: false);
        var recordings = await _discoveryService.GetRecordingDevicesAsync(includeInactive: false);

        // Heuristic detection: VB-Audio Cable, Virtual Audio Cable, or devices classified as VirtualAudio
        var virtualRender = playbacks.FirstOrDefault(d =>
            d.Name.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase) ||
            d.Name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase) ||
            d.AdapterName.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase) ||
            (d.DeviceType == AudioDeviceType.VirtualAudio && d.Name.Contains("Input", StringComparison.OrdinalIgnoreCase))) ??
            playbacks.FirstOrDefault(d => d.DeviceType == AudioDeviceType.VirtualAudio);

        var virtualCapture = recordings.FirstOrDefault(d =>
            d.Name.Contains("CABLE Output", StringComparison.OrdinalIgnoreCase) ||
            d.Name.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase) ||
            d.AdapterName.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase) ||
            (d.DeviceType == AudioDeviceType.VirtualAudio && d.Name.Contains("Output", StringComparison.OrdinalIgnoreCase))) ??
            recordings.FirstOrDefault(d => d.DeviceType == AudioDeviceType.VirtualAudio);

        if (virtualRender == null && virtualCapture == null)
        {
            lock (_lock) _detectedVirtualCable = null;
            return null;
        }

        string driverName = !string.IsNullOrWhiteSpace(virtualRender?.AdapterName)
            ? virtualRender!.AdapterName
            : !string.IsNullOrWhiteSpace(virtualCapture?.AdapterName)
                ? virtualCapture!.AdapterName
                : "VB-Audio Virtual Cable";

        var pair = new VirtualAudioCablePair
        {
            PlaybackDevice = virtualRender,
            RecordingDevice = virtualCapture,
            DriverName = driverName,
            IsCaptureAssigned = string.Equals(virtualCapture?.Id, _settingsService.CurrentSettings.PreferredCaptureDeviceId, StringComparison.OrdinalIgnoreCase)
        };

        lock (_lock) _detectedVirtualCable = pair;
        return pair;
    }

    public void OpenWindowsSoundSettings()
    {
        try
        {
            _logger.LogInformation("Opening Windows Sound Settings (ms-settings:sound)...");
            var psi = new ProcessStartInfo
            {
                FileName = "ms-settings:sound",
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to launch ms-settings:sound. Falling back to mmsys.cpl");
            try
            {
                Process.Start(new ProcessStartInfo("control.exe", "mmsys.cpl") { UseShellExecute = true });
            }
            catch (Exception fallbackEx)
            {
                _logger.LogError(fallbackEx, "Failed to launch control mmsys.cpl");
            }
        }
    }

    public async Task<bool> PlayTestToneAsync(
        AudioOutputBranch? targetBranch = null,
        float durationSeconds = 3.0f,
        float volume = 0.25f,
        CancellationToken cancellationToken = default)
    {
        StopTestTone();

        lock (_lock)
        {
            _testToneCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _isPlayingTestTone = true;
        }

        var token = _testToneCts.Token;

        try
        {
            _logger.LogInformation("Starting safe test tone (duration: {Duration}s, target: {Target})",
                durationSeconds, targetBranch?.ToString() ?? "Both Branches");

            // If audio engine is currently running, feed tone directly through DSP pipeline
            if (_outputService.State == AudioEngineState.Running)
            {
                await RunTestToneViaEngineAsync(targetBranch, durationSeconds, volume, token);
            }
            else
            {
                // Engine is stopped: render directly to selected device endpoints via isolated WasapiOut
                await RunTestToneDirectAsync(targetBranch, durationSeconds, volume, token);
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Test tone stopped by user request");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to play test tone");
            return false;
        }
        finally
        {
            lock (_lock)
            {
                _isPlayingTestTone = false;
            }
        }
    }

    public void StopTestTone()
    {
        lock (_lock)
        {
            if (_testToneCts != null)
            {
                try { _testToneCts.Cancel(); } catch { }
                _testToneCts.Dispose();
                _testToneCts = null;
            }
            _isPlayingTestTone = false;
        }
    }

    private async Task RunTestToneViaEngineAsync(AudioOutputBranch? branch, float durationSeconds, float volume, CancellationToken token)
    {
        var sampleProvider = new SafeTestToneSampleProvider(440f, durationSeconds, volume);
        float[] buffer = new float[960]; // 10 ms chunks at 48 kHz stereo

        while (!sampleProvider.IsComplete && !token.IsCancellationRequested)
        {
            int read = sampleProvider.Read(buffer.AsSpan());
            if (read <= 0) break;

            if (branch == null)
            {
                _outputService.FeedCapturedAudio(buffer.AsSpan(0, read));
            }
            else
            {
                _outputService.FeedBranchAudio(branch.Value, buffer.AsSpan(0, read));
            }

            await Task.Delay(10, token);
        }
    }

    private async Task RunTestToneDirectAsync(AudioOutputBranch? branch, float durationSeconds, float volume, CancellationToken token)
    {
        var settings = _settingsService.CurrentSettings;
        var targets = new List<string>();

        var tvId = settings.TvBranch?.DeviceId ?? settings.PreferredPrimaryDeviceId;
        var htId = settings.HomeTheatreBranch?.DeviceId ?? settings.PreferredSecondaryDeviceId;

        if (branch is null or AudioOutputBranch.TvHdmi && !string.IsNullOrWhiteSpace(tvId)) targets.Add(tvId);
        if (branch is null or AudioOutputBranch.HomeTheatre35mm && !string.IsNullOrWhiteSpace(htId)) targets.Add(htId);

        if (targets.Count == 0)
        {
            _logger.LogWarning("No target physical audio devices configured for direct test tone");
            return;
        }

        var clients = new List<(WasapiOut Out, MMDevice Device)>();
        using var enumerator = new MMDeviceEnumerator();

        try
        {
            foreach (var id in targets)
            {
                try
                {
                    var mmDev = enumerator.GetDevice(id);
                    var wasapi = new WasapiOut(mmDev, AudioClientShareMode.Shared, false, 50);
                    var toneProvider = new SafeTestToneSampleProvider(440f, durationSeconds, volume, mmDev.AudioClient.MixFormat.SampleRate);
                    wasapi.Init(new SampleToWaveProvider(toneProvider));
                    clients.Add((wasapi, mmDev));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not open test device {Id}", id);
                }
            }

            if (clients.Count == 0) return;

            foreach (var (w, _) in clients) w.Play();

            int waitMs = (int)(durationSeconds * 1000);
            await Task.Delay(waitMs, token);
        }
        finally
        {
            foreach (var (w, d) in clients)
            {
                try { w.Stop(); w.Dispose(); } catch { }
                try { d.Dispose(); } catch { }
            }
        }
    }

    private void UpdateStatus(SystemAudioRoutingStatus status)
    {
        lock (_lock)
        {
            _currentStatus = status;
        }
        RoutingStatusChanged?.Invoke(this, status);
    }

    private void OnAudioDeviceChanged(object? sender, DeviceChangeEventArgs e)
    {
        // When default playback device changes or hardware endpoints are plugged/unplugged, revalidate immediately
        _logger.LogInformation("Audio endpoint change detected ({ChangeType}). Revalidating desktop routing...", e.ChangeType);
        _ = ValidateRoutingAsync();
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            _monitorService.DeviceChanged -= OnAudioDeviceChanged;
            StopTestTone();
        }
    }
}
