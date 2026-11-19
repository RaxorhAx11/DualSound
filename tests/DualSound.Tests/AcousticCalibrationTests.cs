using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DualSound.App.ViewModels;
using DualSound.Audio.Calibration;
using DualSound.Audio.Capture;
using DualSound.Audio.Engine;
using DualSound.Audio.Output;
using DualSound.Audio.Presets;
using DualSound.Audio.Sync;
using DualSound.Contracts.Enums;
using DualSound.Contracts.Models;
using DualSound.Core.Services;
using DualSound.Core.ViewModels;
using DualSound.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

#pragma warning disable CS0067

namespace DualSound.Tests;

/// <summary>
/// Automated synthetic and mocked unit tests for Acoustic Sync Calibration.
/// NOTE: These tests verify mathematical correlation algorithms, time-of-flight estimation,
/// confidence scoring, outlier rejection, privacy fallbacks, and settings preservation.
/// These tests run synthetically and do NOT require physical microphones or room acoustics.
/// Actual acoustic validation requires real physical speaker hardware and an active microphone in a room.
/// </summary>
public class AcousticCalibrationTests
{
    private const int SampleRate = 48000;

    [Fact]
    public void CalibrationSignal_EnforcesSafeAmplitudeAndSmoothCosineRamps()
    {
        float[] chirp = AcousticCalibrationSignal.GenerateReferenceChirp(
            sampleRate: SampleRate,
            startFreq: 400f,
            endFreq: 3000f,
            durationSeconds: 0.20f,
            amplitude: 0.20f);

        Assert.Equal((int)(0.20f * SampleRate), chirp.Length);

        // Verify amplitude ceiling (-14 dBFS safe limit)
        float maxAmp = 0f;
        foreach (float s in chirp)
        {
            float abs = Math.Abs(s);
            if (abs > maxAmp) maxAmp = abs;
        }

        Assert.True(maxAmp <= 0.25f, $"Peak amplitude {maxAmp} exceeded safe hardware ceiling");

        // Verify smooth ramp-in: start of waveform must be near 0
        Assert.True(Math.Abs(chirp[0]) < 0.01f, "First sample must be close to zero due to cosine ramp");
        Assert.True(Math.Abs(chirp[^1]) < 0.01f, "Last sample must be close to zero due to cosine ramp");
    }

    [Fact]
    public void CrossCorrelation_KnownDelayOffset_EstimatesArrivalAccurately()
    {
        float[] refChirp = AcousticCalibrationSignal.GenerateReferenceChirp(SampleRate);

        // Synthesize a recorded buffer where chirp arrives at exactly 120 ms (5760 samples)
        int arrivalSample = (int)(0.120f * SampleRate);
        float[] recordedBuffer = new float[SampleRate]; // 1.0 second buffer

        Array.Copy(refChirp, 0, recordedBuffer, arrivalSample, refChirp.Length);

        // Detect arrival via cross-correlation engine
        var detection = AcousticCorrelationEngine.DetectArrival(recordedBuffer.AsSpan(), refChirp.AsSpan(), SampleRate);

        Assert.True(detection.IsAcceptable);
        Assert.True(detection.Confidence >= 0.85f);
        Assert.Equal(arrivalSample, detection.PeakSampleIndex);
        Assert.Equal(120.0f, detection.PeakTimeMs, precision: 1);
    }

    [Fact]
    public void CrossCorrelation_HighBackgroundNoise_RejectsLowConfidence()
    {
        float[] refChirp = AcousticCalibrationSignal.GenerateReferenceChirp(SampleRate);
        float[] noisyBuffer = new float[SampleRate];

        // Fill buffer with strong pseudo-random noise (SNR < 0 dB)
        var rng = new Random(42);
        for (int i = 0; i < noisyBuffer.Length; i++)
        {
            noisyBuffer[i] = (float)((rng.NextDouble() * 2.0) - 1.0) * 0.8f;
        }

        // Add chirp at 100 ms with weak amplitude buried in noise
        int arrivalSample = (int)(0.100f * SampleRate);
        for (int i = 0; i < refChirp.Length; i++)
        {
            noisyBuffer[arrivalSample + i] += refChirp[i] * 0.2f;
        }

        var detection = AcousticCorrelationEngine.DetectArrival(noisyBuffer.AsSpan(), refChirp.AsSpan(), SampleRate);

        // Must reject or mark as low confidence due to poor peak-to-noise ratio
        Assert.True(detection.Confidence < 0.65f || !detection.IsAcceptable,
            "Engine must not claim high confidence when buried in intense background noise");
    }

    [Fact]
    public void CrossCorrelation_FaintSignalBelowThreshold_FailsDetection()
    {
        float[] refChirp = AcousticCalibrationSignal.GenerateReferenceChirp(SampleRate);
        float[] faintBuffer = new float[SampleRate];

        // Very faint signal (< -50 dBFS) simulating muted or disconnected mic
        int arrivalSample = (int)(0.150f * SampleRate);
        for (int i = 0; i < refChirp.Length; i++)
        {
            faintBuffer[arrivalSample + i] = refChirp[i] * 0.001f;
        }

        var detection = AcousticCorrelationEngine.DetectArrival(faintBuffer.AsSpan(), refChirp.AsSpan(), SampleRate);

        Assert.False(detection.IsAcceptable);
        Assert.Contains("too faint", detection.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MultiPass_OutlierRejection_DiscardsErraticMeasurement()
    {
        // 3 passes: Pass 1 and 2 measured ~25 ms difference; Pass 3 experienced an acoustic reflection glitch (80 ms)
        var passes = new List<CalibrationPassMeasurement>
        {
            new(PassNumber: 1, TvArrivalMs: 125f, HomeTheatreArrivalMs: 100f, DifferenceMs: 25f,
                TvPeakToNoiseRatio: 8.5f, HomeTheatrePeakToNoiseRatio: 9.0f, IsAccepted: true),
            new(PassNumber: 2, TvArrivalMs: 126f, HomeTheatreArrivalMs: 101f, DifferenceMs: 25f,
                TvPeakToNoiseRatio: 8.0f, HomeTheatrePeakToNoiseRatio: 8.8f, IsAccepted: true),
            new(PassNumber: 3, TvArrivalMs: 180f, HomeTheatreArrivalMs: 100f, DifferenceMs: 80f, // Severe room echo anomaly
                TvPeakToNoiseRatio: 7.5f, HomeTheatrePeakToNoiseRatio: 8.5f, IsAccepted: true)
        };

        var result = AcousticCorrelationEngine.CalculateAlignment(passes);

        Assert.True(result.Success);
        Assert.True(result.IsConfidenceAcceptable);
        // Robust average should ignore the 80 ms outlier and recommend ~25 ms
        Assert.Equal(25f, result.RecommendedHomeTheatreDelayMs, precision: 1);
        Assert.Equal(0f, result.RecommendedTvDelayMs);
    }

    [Fact]
    public void AlignmentCalculation_TvSlowerThanHomeTheatre_DelaysFasterHomeTheatre()
    {
        // TV arrives at 145 ms (HDMI processing delay); Home Theatre arrives at 120 ms (faster analog DAC)
        // Difference = +25 ms -> Home Theatre output must be delayed by 25 ms
        var passes = new List<CalibrationPassMeasurement>
        {
            new(1, 145f, 120f, 25f, 9f, 9f, true),
            new(2, 145f, 120f, 25f, 9f, 9f, true)
        };

        var result = AcousticCorrelationEngine.CalculateAlignment(passes);

        Assert.True(result.Success);
        Assert.Equal(25f, result.RecommendedHomeTheatreDelayMs);
        Assert.Equal(0f, result.RecommendedTvDelayMs);
        Assert.Contains("Home Theatre", result.Message);
    }

    [Fact]
    public void AlignmentCalculation_HomeTheatreSlowerThanTv_DelaysFasterTv()
    {
        // Home Theatre arrives at 150 ms; TV arrives at 110 ms -> TV is 40 ms faster
        var passes = new List<CalibrationPassMeasurement>
        {
            new(1, 110f, 150f, -40f, 9f, 9f, true),
            new(2, 110f, 150f, -40f, 9f, 9f, true)
        };

        var result = AcousticCorrelationEngine.CalculateAlignment(passes);

        Assert.True(result.Success);
        Assert.Equal(40f, result.RecommendedTvDelayMs);
        Assert.Equal(0f, result.RecommendedHomeTheatreDelayMs);
        Assert.Contains("TV", result.Message);
    }

    [Fact]
    public void AlignmentCalculation_OutputsAlreadySynchronized_RecommendsZeroDelay()
    {
        // Acoustic arrival difference is within 0.5 ms
        var passes = new List<CalibrationPassMeasurement>
        {
            new(1, 100.2f, 100.0f, 0.2f, 9f, 9f, true),
            new(2, 100.1f, 100.0f, 0.1f, 9f, 9f, true)
        };

        var result = AcousticCorrelationEngine.CalculateAlignment(passes);

        Assert.True(result.Success);
        Assert.Equal(0f, result.RecommendedTvDelayMs);
        Assert.Equal(0f, result.RecommendedHomeTheatreDelayMs);
        Assert.Contains("synchronized", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FailedCalibration_PreservesPreviousWorkingSettings()
    {
        using var syncManager = new AudioSyncManager();
        var settingsService = new JsonSettingsService(new NullLogger<JsonSettingsService>());
        settingsService.CurrentSettings.TvBranch.DelayMilliseconds = 18f;
        settingsService.CurrentSettings.HomeTheatreBranch.DelayMilliseconds = 34f;

        var mockCalibrationService = new MockFailingCalibrationService();
        var mockLiveSync = new MockLiveSyncService();
        var mockAudioEngine = CreateMockAudioEngine(syncManager);
        var navService = new TestNavigationService();
        var dispatcher = new TestDispatcherService();

        var vm = new AutomaticSyncViewModel(
            mockCalibrationService,
            mockLiveSync,
            syncManager,
            settingsService,
            mockAudioEngine,
            navService,
            dispatcher,
            new NullLogger<AutomaticSyncViewModel>());

        // Initial delays reflect pre-calibration state
        Assert.Equal(18f, vm.TvDelayMs);
        Assert.Equal(34f, vm.HomeTheatreDelayMs);

        // Trigger calibration that fails
        await vm.StartCalibrationAsync();

        // Verify previous working settings were strictly preserved and not overwritten
        Assert.Equal(18f, vm.TvDelayMs);
        Assert.Equal(34f, vm.HomeTheatreDelayMs);
        Assert.False(vm.CanSaveCalibration);
        Assert.Equal(18f, syncManager.GetDelay(AudioOutputBranch.TvHdmi));
        Assert.Equal(34f, syncManager.GetDelay(AudioOutputBranch.HomeTheatre35mm));
    }

    [Fact]
    public async Task MicrophoneMissing_ReportsClearPrivacyExplanationAndRetainsManualSync()
    {
        var discovery = new MockNoMicDiscoveryService();
        using var syncManager = new AudioSyncManager();
        var settings = new JsonSettingsService(new NullLogger<JsonSettingsService>());
        var outputService = new DualAudioOutputService(
            syncManager,
            new NullLogger<DualAudioOutputService>(),
            new NullLogger<AudioOutputBranchService>());

        var calibrationService = new AcousticCalibrationService(
            discovery,
            settings,
            outputService,
            syncManager,
            new NullLogger<AcousticCalibrationService>());

        var result = await calibrationService.RunCalibrationAsync(null);

        Assert.False(result.Success);
        Assert.Contains("microphone", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Privacy", result.AcousticWarning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Manual synchronization", result.AcousticWarning, StringComparison.OrdinalIgnoreCase);
    }

    private static IAudioEngineService CreateMockAudioEngine(IAudioSyncManager syncManager)
    {
        var discovery = new WindowsAudioDeviceDiscoveryService(new NullLogger<WindowsAudioDeviceDiscoveryService>());
        var monitor = new WindowsAudioDeviceMonitorService(new NullLogger<WindowsAudioDeviceMonitorService>());
        var settings = new JsonSettingsService(new NullLogger<JsonSettingsService>());
        var outputService = new DualAudioOutputService(
            syncManager,
            new NullLogger<DualAudioOutputService>(),
            new NullLogger<AudioOutputBranchService>());
        var capture = new WasapiCaptureService(discovery, monitor, settings, new NullLogger<WasapiCaptureService>());
        var presets = new AudioPresetService(settings, new NullLogger<AudioPresetService>());

        return new AudioEngineService(
            capture,
            outputService,
            syncManager,
            presets,
            monitor,
            settings,
            new NullLogger<AudioEngineService>());
    }

    private sealed class MockFailingCalibrationService : IAcousticCalibrationService
    {
        public bool IsCalibrating => false;
        public bool IsVerifying => false;
        public event EventHandler<CalibrationProgress>? ProgressChanged;
        public Task<IReadOnlyList<AudioDeviceInfo>> GetAvailableMicrophonesAsync() => Task.FromResult<IReadOnlyList<AudioDeviceInfo>>([]);
        public Task<bool> CheckMicrophoneAccessAsync(string? microphoneDeviceId) => Task.FromResult(false);

        public Task<AcousticCalibrationResult> RunCalibrationAsync(
            string? microphoneDeviceId,
            int totalPasses = 3,
            IProgress<CalibrationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(AcousticCalibrationResult.Failed("Microphone not available.", "Windows Privacy Settings blocked microphone access."));
        }

        public Task PlayVerificationToneAsync(float tvDelayMs, float homeTheatreDelayMs, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Stop() { }
        public void Dispose() { }
    }

    private sealed class MockNoMicDiscoveryService : IAudioDeviceDiscoveryService
    {
        public Task<IReadOnlyList<AudioDeviceInfo>> GetPlaybackDevicesAsync(bool includeInactive = true, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AudioDeviceInfo>>([]);
        public Task<IReadOnlyList<AudioDeviceInfo>> GetRecordingDevicesAsync(bool includeInactive = true, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AudioDeviceInfo>>([]);
        public Task<AudioDeviceInfo?> GetDefaultPlaybackDeviceAsync(CancellationToken cancellationToken = default) => Task.FromResult<AudioDeviceInfo?>(null);
        public Task<AudioDeviceInfo?> GetDefaultRecordingDeviceAsync(CancellationToken cancellationToken = default) => Task.FromResult<AudioDeviceInfo?>(null);
        public Task<AudioDeviceInfo?> GetDeviceByIdAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<AudioDeviceInfo?>(null);
    }

    private sealed class MockLiveSyncService : ILiveSyncMonitoringService
    {
        public bool IsEnabled { get; set; } = false;
        public LiveSyncStatus CurrentStatus => LiveSyncStatus.Disabled;
        public event EventHandler<LiveSyncStatus>? StatusChanged;
        public void Start() { }
        public void Stop() { }
        public void Reset() { }
        public void Dispose() { }
    }

    private sealed class TestNavigationService : INavigationService
    {
        public ViewModelBase? CurrentViewModel => null;
        public event EventHandler? CurrentViewModelChanged;
        public void NavigateTo<TViewModel>() where TViewModel : ViewModelBase { }
        public void NavigateTo(Type viewModelType) { }
    }
}
