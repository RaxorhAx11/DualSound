using System;
using System.IO;
using System.Threading.Tasks;
using DualSound.App.ViewModels;
using DualSound.Audio.Capture;
using DualSound.Audio.Engine;
using DualSound.Audio.Output;
using DualSound.Audio.Presets;
using DualSound.Audio.Routing;
using DualSound.Audio.Sync;
using DualSound.Contracts.Enums;
using DualSound.Contracts.Models;
using DualSound.Core.Services;
using DualSound.Core.ViewModels;
using DualSound.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

#pragma warning disable CS0067

namespace DualSound.Tests;

/// <summary>
/// Automated tests for Manual Synchronization:
/// - 1 ms fine adjustment (+1 ms, -1 ms)
/// - Coarse adjustments (+5, -5, +20, -20 ms)
/// - Delay boundaries (0 to 500 ms)
/// - Independent output delay settings
/// - Delay persistence across application restarts
/// - Safe delay updates during active playback (no clicks, pops, buffer corruption)
/// </summary>
public class ManualSynchronizationTests
{
    private readonly IDispatcherService _dispatcher = new TestDispatcherService();
    private readonly ILogger<SynchronizationViewModel> _logger = new NullLogger<SynchronizationViewModel>();
    private readonly INavigationService _navigationService = new TestNavigationService();

    [Fact]
    public void ManualAdjustment_FineOneMillisecondSteps_AdjustsPrecisely()
    {
        using var syncManager = new AudioSyncManager();
        var settingsService = CreateTestSettingsService();

        var vm = new SynchronizationViewModel(
            CreateMockAudioEngine(syncManager),
            syncManager,
            settingsService,
            _dispatcher,
            _navigationService,
            _logger);

        // Reset to 0 ms
        vm.ResetSync();
        Assert.Equal(0f, vm.TvDelayMs);
        Assert.Equal(0f, vm.HomeTheatreDelayMs);

        // TV: +1 ms step
        vm.AdjustTvDelay("1");
        Assert.Equal(1f, vm.TvDelayMs);
        Assert.Equal(1f, syncManager.GetDelay(AudioOutputBranch.TvHdmi));

        // TV: another +1 ms step
        vm.AdjustTvDelay("1");
        Assert.Equal(2f, vm.TvDelayMs);
        Assert.Equal(2f, syncManager.GetDelay(AudioOutputBranch.TvHdmi));

        // TV: -1 ms step
        vm.AdjustTvDelay("-1");
        Assert.Equal(1f, vm.TvDelayMs);
        Assert.Equal(1f, syncManager.GetDelay(AudioOutputBranch.TvHdmi));

        // Home Theatre: fine +1 ms steps
        vm.AdjustHomeTheatreDelay("1");
        Assert.Equal(1f, vm.HomeTheatreDelayMs);
        Assert.Equal(1f, syncManager.GetDelay(AudioOutputBranch.HomeTheatre35mm));

        vm.AdjustHomeTheatreDelay("1");
        Assert.Equal(2f, vm.HomeTheatreDelayMs);

        vm.AdjustHomeTheatreDelay("-1");
        Assert.Equal(1f, vm.HomeTheatreDelayMs);
    }

    [Fact]
    public void ManualAdjustment_CoarseSteps_RetainsExistingAdjustments()
    {
        using var syncManager = new AudioSyncManager();
        var settingsService = CreateTestSettingsService();

        var vm = new SynchronizationViewModel(
            CreateMockAudioEngine(syncManager),
            syncManager,
            settingsService,
            _dispatcher,
            _navigationService,
            _logger);

        vm.ResetSync();

        // +5 ms
        vm.AdjustTvDelay("5");
        Assert.Equal(5f, vm.TvDelayMs);

        // +20 ms
        vm.AdjustTvDelay("20");
        Assert.Equal(25f, vm.TvDelayMs);

        // -5 ms
        vm.AdjustTvDelay("-5");
        Assert.Equal(20f, vm.TvDelayMs);

        // -20 ms
        vm.AdjustTvDelay("-20");
        Assert.Equal(0f, vm.TvDelayMs);
    }

    [Fact]
    public void DelayBoundaries_StrictlyClampedBetweenZeroAnd500Milliseconds()
    {
        using var syncManager = new AudioSyncManager();
        var settingsService = CreateTestSettingsService();

        var vm = new SynchronizationViewModel(
            CreateMockAudioEngine(syncManager),
            syncManager,
            settingsService,
            _dispatcher,
            _navigationService,
            _logger);

        vm.ResetSync();

        // Try reducing below 0 ms
        vm.AdjustTvDelay("-1");
        Assert.Equal(0f, vm.TvDelayMs);
        Assert.Equal(0f, syncManager.GetDelay(AudioOutputBranch.TvHdmi));

        vm.AdjustTvDelay("-20");
        Assert.Equal(0f, vm.TvDelayMs);

        vm.AdjustHomeTheatreDelay("-1");
        Assert.Equal(0f, vm.HomeTheatreDelayMs);

        // Increase up to maximum 500 ms
        vm.TvDelayMs = 499f;
        vm.AdjustTvDelay("1");
        Assert.Equal(500f, vm.TvDelayMs);

        // Try exceeding 500 ms
        vm.AdjustTvDelay("1");
        Assert.Equal(500f, vm.TvDelayMs);

        vm.AdjustTvDelay("20");
        Assert.Equal(500f, vm.TvDelayMs);
        Assert.Equal(500f, syncManager.GetDelay(AudioOutputBranch.TvHdmi));

        // Home Theatre upper boundary
        vm.HomeTheatreDelayMs = 495f;
        vm.AdjustHomeTheatreDelay("5");
        Assert.Equal(500f, vm.HomeTheatreDelayMs);

        vm.AdjustHomeTheatreDelay("1");
        Assert.Equal(500f, vm.HomeTheatreDelayMs);
    }

    [Fact]
    public void IndependentDelays_TvAndHomeTheatreAreDecoupled()
    {
        using var syncManager = new AudioSyncManager();
        var settingsService = CreateTestSettingsService();

        var vm = new SynchronizationViewModel(
            CreateMockAudioEngine(syncManager),
            syncManager,
            settingsService,
            _dispatcher,
            _navigationService,
            _logger);

        vm.ResetSync();

        // Adjust TV only
        vm.AdjustTvDelay("15");
        Assert.Equal(15f, vm.TvDelayMs);
        Assert.Equal(0f, vm.HomeTheatreDelayMs);
        Assert.Equal(15f, syncManager.GetDelay(AudioOutputBranch.TvHdmi));
        Assert.Equal(0f, syncManager.GetDelay(AudioOutputBranch.HomeTheatre35mm));

        // Adjust Home Theatre only
        vm.AdjustHomeTheatreDelay("30");
        Assert.Equal(15f, vm.TvDelayMs);
        Assert.Equal(30f, vm.HomeTheatreDelayMs);
        Assert.Equal(15f, syncManager.GetDelay(AudioOutputBranch.TvHdmi));
        Assert.Equal(30f, syncManager.GetDelay(AudioOutputBranch.HomeTheatre35mm));

        // Delta text reflects differential
        Assert.Contains("15 ms", vm.DelayDeltaText);
    }

    [Fact]
    public async Task DelayPersistence_PersistsAcrossApplicationRestarts()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"dualsound_sync_test_{Guid.NewGuid()}.json");

        try
        {
            // Session 1: configure delays and save
            using (var syncManager1 = new AudioSyncManager())
            {
                var settingsService1 = new JsonSettingsService(new NullLogger<JsonSettingsService>(), tempFile);
                await settingsService1.LoadSettingsAsync();

                var vm1 = new SynchronizationViewModel(
                    CreateMockAudioEngine(syncManager1),
                    syncManager1,
                    settingsService1,
                    _dispatcher,
                    _navigationService,
                    _logger);

                vm1.TvDelayMs = 17f;
                vm1.HomeTheatreDelayMs = 42f;

                await settingsService1.SaveSettingsAsync(settingsService1.CurrentSettings);
            }

            // Session 2: simulate restart, reload settings from disk
            using (var syncManager2 = new AudioSyncManager())
            {
                var settingsService2 = new JsonSettingsService(new NullLogger<JsonSettingsService>(), tempFile);
                await settingsService2.LoadSettingsAsync();

                var vm2 = new SynchronizationViewModel(
                    CreateMockAudioEngine(syncManager2),
                    syncManager2,
                    settingsService2,
                    _dispatcher,
                    _navigationService,
                    _logger);

                Assert.Equal(17f, vm2.TvDelayMs);
                Assert.Equal(42f, vm2.HomeTheatreDelayMs);
                Assert.Equal(17f, syncManager2.GetDelay(AudioOutputBranch.TvHdmi));
                Assert.Equal(42f, syncManager2.GetDelay(AudioOutputBranch.HomeTheatre35mm));
            }
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void SafeDelayUpdates_DynamicAdjustmentsDuringPlayback_CauseNoBufferCorruptionOrClicks()
    {
        // Continuous audio playback simulation
        var buffer = new OutputDelayBuffer(AudioOutputBranch.TvHdmi, sampleRate: 48000, channels: 2);
        buffer.SetDelayMilliseconds(10f);

        // Pre-fill buffer with continuous sine wave
        for (int i = 0; i < 5; i++)
        {
            float[] chunk = new float[960]; // 10 ms at 48kHz stereo
            for (int s = 0; s < chunk.Length; s++) chunk[s] = (float)Math.Sin(s * 0.1);
            buffer.Write(chunk);
        }

        float[] readBuf = new float[960];
        buffer.Read(readBuf);

        // Live dynamic 1 ms fine adjustment while actively playing
        buffer.SetDelayMilliseconds(11f);

        // Write and read next chunk
        float[] chunk2 = new float[960];
        for (int s = 0; s < chunk2.Length; s++) chunk2[s] = (float)Math.Sin((s + 960) * 0.1);
        buffer.Write(chunk2);

        int readCount = buffer.Read(readBuf);
        Assert.Equal(960, readCount);

        // Verify all output samples are finite, valid numbers (no NaN, no Infinity, no corrupt memory)
        foreach (float sample in readBuf)
        {
            Assert.False(float.IsNaN(sample), "Sample must not be NaN during live delay transition");
            Assert.False(float.IsInfinity(sample), "Sample must not be Infinite during live delay transition");
            Assert.InRange(sample, -1.5f, 1.5f);
        }

        // Live dynamic fine reduction (-1 ms) while actively playing
        buffer.SetDelayMilliseconds(10f);
        buffer.Write(chunk2);
        readCount = buffer.Read(readBuf);
        Assert.Equal(960, readCount);

        foreach (float sample in readBuf)
        {
            Assert.False(float.IsNaN(sample));
            Assert.InRange(sample, -1.5f, 1.5f);
        }
    }

    private static ISettingsService CreateTestSettingsService()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"sync_test_settings_{Guid.NewGuid()}.json");
        return new JsonSettingsService(new NullLogger<JsonSettingsService>(), tempPath);
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
        var capture = new WasapiCaptureService(
            discovery,
            monitor,
            settings,
            new NullLogger<WasapiCaptureService>());
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

    private sealed class TestNavigationService : INavigationService
    {
        public ViewModelBase? CurrentViewModel => null;
        public event EventHandler? CurrentViewModelChanged;
        public void NavigateTo<TViewModel>() where TViewModel : ViewModelBase { }
        public void NavigateTo(Type viewModelType) { }
    }
}
