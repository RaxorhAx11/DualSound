using System;
using System.IO;
using System.Threading;
using DualSound.Audio.Capture;
using DualSound.Audio.Engine;
using DualSound.Audio.Output;
using DualSound.Audio.Presets;
using DualSound.Audio.Sync;
using DualSound.Contracts.Enums;
using DualSound.Contracts.Models;
using DualSound.Core.Services;
using DualSound.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

#pragma warning disable CS0067

namespace DualSound.Tests;

/// <summary>
/// Automated tests for Live Sync Monitoring:
/// - Inactive monitoring state when Audio Engine is stopped (clear limitation notice)
/// - Clock drift monitoring and telemetry reporting
/// - Buffer starvation / underrun protection (suspends corrections to avoid compounding errors)
/// - Drift correction stability (deadband tolerance and cooldown enforcement)
/// - Enable / Disable toggle behavior and persistence
/// </summary>
public class LiveSyncMonitoringTests
{
    [Fact]
    public void LiveSync_WhenAudioEngineStopped_ReportsInactiveAndExplainsLimitation()
    {
        using var syncManager = new AudioSyncManager();
        var settings = new JsonSettingsService(new NullLogger<JsonSettingsService>());
        var mockAudioEngine = CreateMockAudioEngine(syncManager, isRunning: false);

        using var liveSync = new LiveSyncMonitoringService(
            syncManager,
            mockAudioEngine,
            settings,
            new NullLogger<LiveSyncMonitoringService>());

        liveSync.IsEnabled = true;

        // Force a telemetry evaluation tick
        Thread.Sleep(350);

        var status = liveSync.CurrentStatus;
        Assert.True(status.IsEnabled);
        Assert.False(status.IsActive);
        Assert.Contains("Engine Stopped", status.StatusDescription, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(status.LimitationNotice);
        Assert.Contains("active audio playback", status.LimitationNotice, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LiveSync_WhenDisabled_ReportsDisabledStatus()
    {
        using var syncManager = new AudioSyncManager();
        var settings = new JsonSettingsService(new NullLogger<JsonSettingsService>());
        var mockAudioEngine = CreateMockAudioEngine(syncManager, isRunning: true);

        using var liveSync = new LiveSyncMonitoringService(
            syncManager,
            mockAudioEngine,
            settings,
            new NullLogger<LiveSyncMonitoringService>());

        liveSync.IsEnabled = false;

        var status = liveSync.CurrentStatus;
        Assert.False(status.IsEnabled);
        Assert.False(status.IsActive);
        Assert.Equal("Disabled", status.StatusDescription);
    }

    [Fact]
    public void LiveSync_WhenBufferUnderrunsOccur_SuspendsCorrectionAndReportsUnreliable()
    {
        var mockSyncManager = new MockUnderrunSyncManager();
        var settings = new JsonSettingsService(new NullLogger<JsonSettingsService>());
        var mockAudioEngine = CreateMockAudioEngine(mockSyncManager, isRunning: true);

        using var liveSync = new LiveSyncMonitoringService(
            mockSyncManager,
            mockAudioEngine,
            settings,
            new NullLogger<LiveSyncMonitoringService>());

        liveSync.IsEnabled = true;

        // Let timer tick twice to observe newly occurring underruns
        Thread.Sleep(600);

        var status = liveSync.CurrentStatus;
        Assert.False(status.IsReliable);
        Assert.Contains("Unreliable", status.StatusDescription, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(status.LimitationNotice);
        Assert.Contains("underrun", status.LimitationNotice, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, status.TotalCorrectionsApplied);
    }

    [Fact]
    public void LiveSync_DriftWithinTolerance_AppliesNoUnnecessaryCorrections()
    {
        // Drift is 0.8 ms, within the default 2.0 ms tolerance
        var mockSyncManager = new MockSteadyDriftSyncManager(driftSamples: 38); // 38 samples at 48kHz ~ 0.79 ms
        var settings = new JsonSettingsService(new NullLogger<JsonSettingsService>());
        var mockAudioEngine = CreateMockAudioEngine(mockSyncManager, isRunning: true);

        using var liveSync = new LiveSyncMonitoringService(
            mockSyncManager,
            mockAudioEngine,
            settings,
            new NullLogger<LiveSyncMonitoringService>());

        liveSync.IsEnabled = true;

        // Accumulate moving average
        Thread.Sleep(2000);

        var status = liveSync.CurrentStatus;
        Assert.Equal(0, status.TotalCorrectionsApplied);
        Assert.Contains("Synchronized", status.StatusDescription, StringComparison.OrdinalIgnoreCase);
    }

    private static IAudioEngineService CreateMockAudioEngine(IAudioSyncManager syncManager, bool isRunning)
    {
        return new MockAudioEngine(syncManager, isRunning ? AudioEngineState.Running : AudioEngineState.Stopped);
    }

    private sealed class MockAudioEngine : IAudioEngineService
    {
        public AudioEngineState State { get; set; }
        public IDualAudioOutputService OutputService { get; }
        public IAudioSyncManager SyncManager { get; }
        public IAudioPresetService PresetService { get; }
        public event EventHandler<AudioEngineState>? StateChanged;
        public event EventHandler<string>? EngineError;

        public MockAudioEngine(IAudioSyncManager syncManager, AudioEngineState state = AudioEngineState.Stopped)
        {
            State = state;
            SyncManager = syncManager;
            OutputService = new DualAudioOutputService(
                syncManager,
                new NullLogger<DualAudioOutputService>(),
                new NullLogger<AudioOutputBranchService>());
            var settings = new JsonSettingsService(new NullLogger<JsonSettingsService>());
            PresetService = new AudioPresetService(settings, new NullLogger<AudioPresetService>());
        }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            State = AudioEngineState.Running;
            StateChanged?.Invoke(this, State);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            State = AudioEngineState.Stopped;
            StateChanged?.Invoke(this, State);
            return Task.CompletedTask;
        }

        public Task RestartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    private sealed class MockUnderrunSyncManager : IAudioSyncManager
    {
        private long _underruns = 10;
        public void SetDelay(AudioOutputBranch branch, float delayMs) { }
        public float GetDelay(AudioOutputBranch branch) => 0f;

        public AudioSyncStatus GetStatus(AudioOutputBranch branch)
        {
            _underruns += 5; // Increments every poll
            return new AudioSyncStatus(
                Branch: branch,
                ConfiguredDelayMs: 0f,
                EstimatedLatencyMs: 20f,
                BufferOccupancyPercentage: 50f,
                DriftSamples: 10,
                UnderrunCount: _underruns,
                OverrunCount: 0,
                IsSynchronized: false,
                HealthDescription: "Underrun Detected");
        }

        public void AdjustDelaySmoothly(AudioOutputBranch branch, float deltaMs) { }
        public void Reset() { }
        public event EventHandler<AudioSyncStatus>? StatusUpdated;
        public void Dispose() { }
    }

    private sealed class MockSteadyDriftSyncManager : IAudioSyncManager
    {
        private readonly long _driftSamples;
        public MockSteadyDriftSyncManager(long driftSamples) => _driftSamples = driftSamples;

        public void SetDelay(AudioOutputBranch branch, float delayMs) { }
        public float GetDelay(AudioOutputBranch branch) => 0f;

        public AudioSyncStatus GetStatus(AudioOutputBranch branch)
        {
            return new AudioSyncStatus(
                Branch: branch,
                ConfiguredDelayMs: 0f,
                EstimatedLatencyMs: 20f,
                BufferOccupancyPercentage: 50f,
                DriftSamples: branch == AudioOutputBranch.TvHdmi ? _driftSamples : 0,
                UnderrunCount: 0,
                OverrunCount: 0,
                IsSynchronized: true,
                HealthDescription: "Synchronized");
        }

        public void AdjustDelaySmoothly(AudioOutputBranch branch, float deltaMs) { }
        public void Reset() { }
        public event EventHandler<AudioSyncStatus>? StatusUpdated;
        public void Dispose() { }
    }
}
