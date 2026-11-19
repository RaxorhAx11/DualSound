using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DualSound.Contracts.Enums;
using DualSound.Contracts.Models;
using DualSound.Core.Services;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

#pragma warning disable CS0618

namespace DualSound.Audio.Calibration;

/// <summary>
/// Production service executing acoustic synchronization calibration using physical microphone capture
/// and matched-filter cross-correlation between HDMI TV and 3.5mm Home Theatre sound outputs.
/// </summary>
public sealed class AcousticCalibrationService : IAcousticCalibrationService
{
    private readonly IAudioDeviceDiscoveryService _discoveryService;
    private readonly ISettingsService _settingsService;
    private readonly IDualAudioOutputService _outputService;
    private readonly IAudioSyncManager _syncManager;
    private readonly ILogger<AcousticCalibrationService> _logger;
    private readonly object _stateLock = new();

    private CancellationTokenSource? _activeCts;
    private bool _isCalibrating;
    private bool _isVerifying;
    private bool _isDisposed;

    public bool IsCalibrating
    {
        get { lock (_stateLock) return _isCalibrating; }
    }

    public bool IsVerifying
    {
        get { lock (_stateLock) return _isVerifying; }
    }

    public event EventHandler<CalibrationProgress>? ProgressChanged;

    public AcousticCalibrationService(
        IAudioDeviceDiscoveryService discoveryService,
        ISettingsService settingsService,
        IDualAudioOutputService outputService,
        IAudioSyncManager syncManager,
        ILogger<AcousticCalibrationService> logger)
    {
        _discoveryService = discoveryService;
        _settingsService = settingsService;
        _outputService = outputService;
        _syncManager = syncManager;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AudioDeviceInfo>> GetAvailableMicrophonesAsync()
    {
        try
        {
            var recordings = await _discoveryService.GetRecordingDevicesAsync(includeInactive: false);
            // Filter to genuine physical microphones or audio inputs (exclude virtual audio cables)
            var mics = recordings.Where(d =>
                !d.Name.Contains("CABLE Output", StringComparison.OrdinalIgnoreCase) &&
                !d.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase) &&
                !d.AdapterName.Contains("VB-Audio", StringComparison.OrdinalIgnoreCase) &&
                d.IsActive).ToList();

            // If no physical mics were classified, return all active non-virtual recording endpoints
            if (mics.Count == 0)
            {
                mics = recordings.Where(d => d.IsActive && !d.Name.Contains("CABLE", StringComparison.OrdinalIgnoreCase)).ToList();
            }

            return mics;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enumerate microphones");
            return [];
        }
    }

    public async Task<bool> CheckMicrophoneAccessAsync(string? microphoneDeviceId)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                MMDevice? mmDev = null;

                if (!string.IsNullOrWhiteSpace(microphoneDeviceId))
                {
                    try { mmDev = enumerator.GetDevice(microphoneDeviceId); } catch { mmDev = null; }
                }

                mmDev ??= enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
                if (mmDev == null) return false;

                using var capture = new WasapiCapture(mmDev, false, 50);
                capture.StartRecording();
                Thread.Sleep(50);
                capture.StopRecording();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Microphone access check failed for device '{DeviceId}'", microphoneDeviceId);
                return false;
            }
        });
    }

    public async Task<AcousticCalibrationResult> RunCalibrationAsync(
        string? microphoneDeviceId,
        int totalPasses = 3,
        IProgress<CalibrationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (_isCalibrating) throw new InvalidOperationException("Calibration is already in progress.");
            _isCalibrating = true;
            _activeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        var token = _activeCts.Token;

        try
        {
            ReportProgress(progress, CalibrationStage.PositioningNotice, 0, totalPasses, 5f,
                "Positioning: Ensure microphone is placed at your normal listening location.");
            await Task.Delay(300, token);

            // Step 1: Detect and verify microphone
            ReportProgress(progress, CalibrationStage.CheckingMicrophone, 0, totalPasses, 10f,
                "Checking microphone availability and privacy permissions...");

            var availableMics = await GetAvailableMicrophonesAsync();
            if (availableMics.Count == 0)
            {
                return AcousticCalibrationResult.Failed(
                    "No usable microphone detected.",
                    "Connect a microphone, headset, or enable your laptop microphone. If already connected, check Windows Settings > Privacy & security > Microphone to ensure access is allowed. Manual synchronization remains fully available.");
            }

            string? selectedId = microphoneDeviceId;
            if (string.IsNullOrWhiteSpace(selectedId) || !availableMics.Any(m => m.Id == selectedId))
            {
                selectedId = availableMics.FirstOrDefault(m => m.IsDefaultRecording)?.Id ?? availableMics.First().Id;
            }

            bool micAccessible = await CheckMicrophoneAccessAsync(selectedId);
            if (!micAccessible)
            {
                return AcousticCalibrationResult.Failed(
                    "Microphone access denied or audio capture blocked.",
                    "Windows privacy settings may be preventing microphone access. Open Windows Settings > Privacy & security > Microphone and ensure 'Let desktop apps access your microphone' is turned ON. Manual synchronization remains fully available.");
            }

            // Step 2: Prepare reference chirp signals
            const int sampleRate = 48000;
            float[] refChirp = AcousticCalibrationSignal.GenerateReferenceChirp(sampleRate);
            float[] stereoChirp = AcousticCalibrationSignal.GenerateStereoChirp(sampleRate);

            // Measure ambient noise floor
            ReportProgress(progress, CalibrationStage.CheckingMicrophone, 0, totalPasses, 15f,
                "Measuring ambient room noise floor...");
            float ambientNoiseDb = await MeasureAmbientNoiseAsync(selectedId, token);

            if (ambientNoiseDb > AcousticCorrelationEngine.MaxAmbientNoiseFloorDb)
            {
                _logger.LogWarning("Ambient noise floor high: {NoiseDb:F1} dBFS", ambientNoiseDb);
            }

            var passes = new List<CalibrationPassMeasurement>();

            // Step 3: Run multi-pass acoustic calibration
            for (int pass = 1; pass <= totalPasses; pass++)
            {
                token.ThrowIfCancellationRequested();

                float baseProgress = 15f + ((float)(pass - 1) / totalPasses) * 75f;

                // 3a. TV Output Calibration Chirp
                ReportProgress(progress, CalibrationStage.MeasuringTv, pass, totalPasses, baseProgress + 10f,
                    $"Pass {pass} of {totalPasses}: Playing safe test chirp through TV (HDMI) only...");

                var tvRecorded = await PlayChirpAndRecordAsync(AudioOutputBranch.TvHdmi, stereoChirp, selectedId, token);

                // Small acoustic reverberation settling delay
                await Task.Delay(250, token);

                // 3b. Home Theatre Output Calibration Chirp
                ReportProgress(progress, CalibrationStage.MeasuringHomeTheatre, pass, totalPasses, baseProgress + 20f,
                    $"Pass {pass} of {totalPasses}: Playing safe test chirp through Home Theatre only...");

                var htRecorded = await PlayChirpAndRecordAsync(AudioOutputBranch.HomeTheatre35mm, stereoChirp, selectedId, token);

                // 3c. Matched Filter Cross-Correlation
                ReportProgress(progress, CalibrationStage.AnalyzingCorrelation, pass, totalPasses, baseProgress + 25f,
                    $"Pass {pass} of {totalPasses}: Calculating acoustic time-of-flight cross-correlation...");

                var tvDetection = AcousticCorrelationEngine.DetectArrival(tvRecorded.AsSpan(), refChirp.AsSpan(), sampleRate);
                var htDetection = AcousticCorrelationEngine.DetectArrival(htRecorded.AsSpan(), refChirp.AsSpan(), sampleRate);

                float diffMs = tvDetection.PeakTimeMs - htDetection.PeakTimeMs;
                bool passAccepted = tvDetection.IsAcceptable && htDetection.IsAcceptable;
                string? rejectReason = !tvDetection.IsAcceptable ? $"TV: {tvDetection.FailureReason}"
                                      : !htDetection.IsAcceptable ? $"Home Theatre: {htDetection.FailureReason}" : null;

                passes.Add(new CalibrationPassMeasurement(
                    PassNumber: pass,
                    TvArrivalMs: tvDetection.PeakTimeMs,
                    HomeTheatreArrivalMs: htDetection.PeakTimeMs,
                    DifferenceMs: diffMs,
                    TvPeakToNoiseRatio: tvDetection.PeakToNoiseRatio,
                    HomeTheatrePeakToNoiseRatio: htDetection.PeakToNoiseRatio,
                    IsAccepted: passAccepted,
                    RejectionReason: rejectReason));

                await Task.Delay(150, token);
            }

            // Step 4: Robust averaging & alignment synthesis
            ReportProgress(progress, CalibrationStage.AnalyzingCorrelation, totalPasses, totalPasses, 95f,
                "Analyzing measurements and filtering room reflections...");

            var result = AcousticCorrelationEngine.CalculateAlignment(passes, ambientNoiseDb);

            if (result.Success)
            {
                ReportProgress(progress, CalibrationStage.Completed, totalPasses, totalPasses, 100f,
                    $"Calibration successful ({result.ConfidenceScore * 100:F0}% confidence). {result.Message}");
            }
            else
            {
                ReportProgress(progress, CalibrationStage.Failed, totalPasses, totalPasses, 100f,
                    $"Calibration unreliable: {result.Message}");
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Acoustic calibration cancelled by user");
            ReportProgress(progress, CalibrationStage.Cancelled, 0, totalPasses, 0f, "Calibration stopped by user. Previous settings preserved.");
            return AcousticCalibrationResult.Failed("Calibration stopped by user.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during acoustic calibration");
            ReportProgress(progress, CalibrationStage.Failed, 0, totalPasses, 0f, $"Calibration failed: {ex.Message}");
            return AcousticCalibrationResult.Failed($"Calibration error: {ex.Message}");
        }
        finally
        {
            lock (_stateLock)
            {
                _isCalibrating = false;
                _activeCts?.Dispose();
                _activeCts = null;
            }
        }
    }

    public async Task PlayVerificationToneAsync(
        float tvDelayMs,
        float homeTheatreDelayMs,
        CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (_isVerifying) return;
            _isVerifying = true;
        }

        try
        {
            _logger.LogInformation("Playing verification chirp: TV delay {TvDelay} ms, HT delay {HtDelay} ms",
                tvDelayMs, homeTheatreDelayMs);

            // Temporarily apply verification delays
            float origTv = _syncManager.GetDelay(AudioOutputBranch.TvHdmi);
            float origHt = _syncManager.GetDelay(AudioOutputBranch.HomeTheatre35mm);

            _syncManager.SetDelay(AudioOutputBranch.TvHdmi, tvDelayMs);
            _syncManager.SetDelay(AudioOutputBranch.HomeTheatre35mm, homeTheatreDelayMs);

            const int sampleRate = 48000;
            float[] stereoChirp = AcousticCalibrationSignal.GenerateStereoChirp(sampleRate, durationSeconds: 0.25f, amplitude: 0.20f);

            // Play simultaneously through both branches
            if (_outputService.State == AudioEngineState.Running)
            {
                _outputService.FeedBranchAudio(AudioOutputBranch.TvHdmi, stereoChirp.AsSpan());
                _outputService.FeedBranchAudio(AudioOutputBranch.HomeTheatre35mm, stereoChirp.AsSpan());
                await Task.Delay(400, cancellationToken);
            }
            else
            {
                await PlayDirectToDevicesAsync(null, stereoChirp, cancellationToken);
            }

            // Restore original delays so user must explicitly click "Save Calibration" to commit!
            _syncManager.SetDelay(AudioOutputBranch.TvHdmi, origTv);
            _syncManager.SetDelay(AudioOutputBranch.HomeTheatre35mm, origHt);
        }
        finally
        {
            lock (_stateLock)
            {
                _isVerifying = false;
            }
        }
    }

    public void Stop()
    {
        lock (_stateLock)
        {
            if (_activeCts != null)
            {
                try { _activeCts.Cancel(); } catch { }
            }
            _isCalibrating = false;
            _isVerifying = false;
        }
    }

    private async Task<float[]> PlayChirpAndRecordAsync(
        AudioOutputBranch branch,
        float[] stereoChirp,
        string microphoneDeviceId,
        CancellationToken token)
    {
        using var enumerator = new MMDeviceEnumerator();
        var micDevice = enumerator.GetDevice(microphoneDeviceId);

        // Record for 750 ms (chirp + acoustic arrival search window)
        const int recordMs = 750;
        var recordedSamples = new List<float>(36000);
        var recordCompletedTcs = new TaskCompletionSource<bool>();

        using var capture = new WasapiCapture(micDevice, false, 20);
        var captureFormat = capture.WaveFormat;

        capture.DataAvailable += (s, e) =>
        {
            int bytesPerSample = captureFormat.BitsPerSample / 8;
            int channels = captureFormat.Channels;
            int samples = e.BytesRecorded / bytesPerSample;

            for (int i = 0; i < samples; i += channels)
            {
                float sample;
                if (captureFormat.Encoding == WaveFormatEncoding.IeeeFloat)
                {
                    sample = BitConverter.ToSingle(e.Buffer, i * bytesPerSample);
                }
                else if (captureFormat.BitsPerSample == 16)
                {
                    short pcm = BitConverter.ToInt16(e.Buffer, i * bytesPerSample);
                    sample = pcm / 32768.0f;
                }
                else
                {
                    sample = 0f;
                }

                recordedSamples.Add(sample);
            }
        };

        capture.StartRecording();

        try
        {
            // Allow 50 ms pre-buffering
            await Task.Delay(50, token);

            // Play chirp on requested branch
            if (_outputService.State == AudioEngineState.Running)
            {
                _outputService.FeedBranchAudio(branch, stereoChirp.AsSpan());
            }
            else
            {
                _ = Task.Run(() => PlayDirectToDevicesAsync(branch, stereoChirp, token), token);
            }

            await Task.Delay(recordMs, token);
        }
        finally
        {
            try { capture.StopRecording(); } catch { }
        }

        return recordedSamples.ToArray();
    }

    private async Task<float> MeasureAmbientNoiseAsync(string microphoneDeviceId, CancellationToken token)
    {
        using var enumerator = new MMDeviceEnumerator();
        var micDevice = enumerator.GetDevice(microphoneDeviceId);
        var samples = new List<float>(4800);

        using var capture = new WasapiCapture(micDevice, false, 20);
        var captureFormat = capture.WaveFormat;

        capture.DataAvailable += (s, e) =>
        {
            int bytesPerSample = captureFormat.BitsPerSample / 8;
            int channels = captureFormat.Channels;
            int sampleCount = e.BytesRecorded / bytesPerSample;

            for (int i = 0; i < sampleCount; i += channels)
            {
                float val = captureFormat.Encoding == WaveFormatEncoding.IeeeFloat
                    ? BitConverter.ToSingle(e.Buffer, i * bytesPerSample)
                    : BitConverter.ToInt16(e.Buffer, i * bytesPerSample) / 32768f;

                samples.Add(val);
            }
        };

        capture.StartRecording();
        await Task.Delay(150, token);
        try { capture.StopRecording(); } catch { }

        return AcousticCorrelationEngine.MeasureAmbientNoiseFloorDb(samples.ToArray().AsSpan());
    }

    private async Task PlayDirectToDevicesAsync(AudioOutputBranch? targetBranch, float[] stereoChirp, CancellationToken token)
    {
        var settings = _settingsService.CurrentSettings;
        var deviceIds = new List<string>();

        var tvId = settings.TvBranch?.DeviceId ?? settings.PreferredPrimaryDeviceId;
        var htId = settings.HomeTheatreBranch?.DeviceId ?? settings.PreferredSecondaryDeviceId;

        if (targetBranch is null or AudioOutputBranch.TvHdmi && !string.IsNullOrWhiteSpace(tvId)) deviceIds.Add(tvId);
        if (targetBranch is null or AudioOutputBranch.HomeTheatre35mm && !string.IsNullOrWhiteSpace(htId)) deviceIds.Add(htId);

        if (deviceIds.Count == 0) return;

        using var enumerator = new MMDeviceEnumerator();
        var clients = new List<(WasapiOut Out, MMDevice Dev)>();

        try
        {
            foreach (var id in deviceIds)
            {
                try
                {
                    var mmDev = enumerator.GetDevice(id);
                    var wasapi = new WasapiOut(mmDev, AudioClientShareMode.Shared, false, 50);
                    var provider = new AcousticCalibrationSignal.ChirpSampleProvider(stereoChirp, mmDev.AudioClient.MixFormat.SampleRate);
                    wasapi.Init(new SampleToWaveProvider(provider));
                    clients.Add((wasapi, mmDev));
                }
                catch { }
            }

            foreach (var (w, _) in clients) w.Play();
            await Task.Delay(350, token);
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

    private void ReportProgress(
        IProgress<CalibrationProgress>? progress,
        CalibrationStage stage,
        int currentPass,
        int totalPasses,
        float percentage,
        string message)
    {
        var p = new CalibrationProgress(stage, currentPass, totalPasses, percentage, message);
        ProgressChanged?.Invoke(this, p);
        progress?.Report(p);
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            Stop();
        }
    }
}
