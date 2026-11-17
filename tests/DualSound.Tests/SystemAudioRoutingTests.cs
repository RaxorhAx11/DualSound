using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DualSound.Audio.Routing;
using DualSound.Audio.Sync;
using DualSound.Contracts.Enums;
using DualSound.Contracts.Events;
using DualSound.Contracts.Models;
using DualSound.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

#pragma warning disable CS0067

namespace DualSound.Tests;

public class SystemAudioRoutingTests
{
    [Fact]
    public void SafeTestTone_EnforcesSafetyCeiling_AndSmoothCosineRamp()
    {
        // Request excessive 1.0 volume (0 dBFS), provider must enforce safe <= 0.5 ceiling (-6 dBFS max)
        var provider = new SafeTestToneSampleProvider(frequency: 440f, durationSeconds: 0.5f, amplitude: 1.0f, sampleRate: 48000);

        Assert.Equal(48000, provider.WaveFormat.SampleRate);
        Assert.Equal(2, provider.WaveFormat.Channels);
        Assert.False(provider.IsComplete);

        float[] buffer = new float[960]; // 10 ms
        float maxSample = 0f;

        while (!provider.IsComplete)
        {
            int read = provider.Read(buffer, 0, buffer.Length);
            if (read <= 0) break;

            for (int i = 0; i < read; i++)
            {
                float abs = Math.Abs(buffer[i]);
                if (abs > maxSample) maxSample = abs;
            }
        }

        Assert.True(provider.IsComplete);
        // Absolute peak amplitude should never exceed 0.5f (safe listening ceiling)
        Assert.InRange(maxSample, 0.45f, 0.5001f);
    }

    [Fact]
    public void SafeTestTone_SoftRampIn_StartsAtZero()
    {
        var provider = new SafeTestToneSampleProvider(frequency: 440f, durationSeconds: 1.0f, amplitude: 0.25f, sampleRate: 48000);
        float[] buffer = new float[100];
        int read = provider.Read(buffer, 0, buffer.Length);

        Assert.True(read > 0);
        // First few samples during ramp-in must be close to zero, avoiding abrupt pops
        Assert.True(Math.Abs(buffer[0]) < 0.05f);
        Assert.True(Math.Abs(buffer[1]) < 0.05f);
    }

    [Fact]
    public async Task ValidateRouting_WhenVirtualCableMissing_ReturnsVirtualCableMissingState()
    {
        var discovery = new FakeAudioDiscoveryService();
        discovery.PlaybackDevices.Add(CreateDevice("tv-1", "LG TV", AudioDeviceType.HdmiDisplayPort));
        discovery.PlaybackDevices.Add(CreateDevice("ht-1", "Realtek Audio", AudioDeviceType.LineOut35mm));
        discovery.DefaultPlayback = discovery.PlaybackDevices[0];

        var settings = new FakeSettingsService();
        settings.CurrentSettings.PreferredPrimaryDeviceId = "tv-1";
        settings.CurrentSettings.PreferredSecondaryDeviceId = "ht-1";

        using var service = new SystemAudioRoutingService(
            discovery,
            new FakeAudioMonitorService(),
            settings,
            new FakeDualAudioOutputService(),
            NullLogger<SystemAudioRoutingService>.Instance);

        var status = await service.ValidateRoutingAsync();

        Assert.Equal(RoutingValidationState.VirtualCableMissing, status.ValidationState);
        Assert.False(status.IsReady);
        Assert.Contains("Virtual Audio Cable Not Found", status.StatusTitle);
    }

    [Fact]
    public async Task ValidateRouting_WhenDefaultPlaybackNotVirtualCable_ReturnsDefaultDeviceMismatch()
    {
        var discovery = new FakeAudioDiscoveryService();
        var tv = CreateDevice("tv-1", "LG TV (HDMI)", AudioDeviceType.HdmiDisplayPort);
        var ht = CreateDevice("ht-1", "Speakers (3.5mm)", AudioDeviceType.LineOut35mm);
        var cableIn = CreateDevice("cable-in", "CABLE Input (VB-Audio Virtual Cable)", AudioDeviceType.VirtualAudio);
        var cableOut = CreateDevice("cable-out", "CABLE Output (VB-Audio Virtual Cable)", AudioDeviceType.VirtualAudio);

        discovery.PlaybackDevices.AddRange([tv, ht, cableIn]);
        discovery.RecordingDevices.Add(cableOut);
        // Default is TV, bypassing the virtual cable
        discovery.DefaultPlayback = tv;

        var settings = new FakeSettingsService();
        settings.CurrentSettings.TvBranch.DeviceId = "tv-1";
        settings.CurrentSettings.HomeTheatreBranch.DeviceId = "ht-1";
        settings.CurrentSettings.PreferredPrimaryDeviceId = "tv-1";
        settings.CurrentSettings.PreferredSecondaryDeviceId = "ht-1";
        settings.CurrentSettings.PreferredCaptureDeviceId = "cable-out";

        using var service = new SystemAudioRoutingService(
            discovery,
            new FakeAudioMonitorService(),
            settings,
            new FakeDualAudioOutputService(),
            NullLogger<SystemAudioRoutingService>.Instance);

        var status = await service.ValidateRoutingAsync();

        Assert.Equal(RoutingValidationState.DefaultDeviceMismatch, status.ValidationState);
        Assert.False(status.IsDesktopAudioRoutedToVirtualCable);
        Assert.False(status.IsReady);
        Assert.Contains("Desktop Audio Not Routed to Virtual Cable", status.StatusTitle);
    }

    [Fact]
    public async Task ValidateRouting_WhenOutputsConflict_ReturnsOutputsConflictState()
    {
        var discovery = new FakeAudioDiscoveryService();
        var sharedDev = CreateDevice("shared-1", "Realtek Speakers", AudioDeviceType.Speakers);
        var cableIn = CreateDevice("cable-in", "CABLE Input (VB-Audio)", AudioDeviceType.VirtualAudio);
        var cableOut = CreateDevice("cable-out", "CABLE Output (VB-Audio)", AudioDeviceType.VirtualAudio);

        discovery.PlaybackDevices.AddRange([sharedDev, cableIn]);
        discovery.RecordingDevices.Add(cableOut);
        discovery.DefaultPlayback = cableIn;

        var settings = new FakeSettingsService();
        // TV and Home Theatre assigned to exact same device
        settings.CurrentSettings.TvBranch.DeviceId = "shared-1";
        settings.CurrentSettings.HomeTheatreBranch.DeviceId = "shared-1";
        settings.CurrentSettings.PreferredCaptureDeviceId = "cable-out";

        using var service = new SystemAudioRoutingService(
            discovery,
            new FakeAudioMonitorService(),
            settings,
            new FakeDualAudioOutputService(),
            NullLogger<SystemAudioRoutingService>.Instance);

        var status = await service.ValidateRoutingAsync();

        Assert.Equal(RoutingValidationState.OutputsConflict, status.ValidationState);
        Assert.False(status.IsReady);
        Assert.Contains("Conflicting Output Assignment", status.StatusTitle);
    }

    [Fact]
    public async Task ValidateRouting_WhenFullyConfigured_ReturnsFullyConfiguredAndReady()
    {
        var discovery = new FakeAudioDiscoveryService();
        var tv = CreateDevice("tv-1", "LG TV (HDMI)", AudioDeviceType.HdmiDisplayPort);
        var ht = CreateDevice("ht-1", "Realtek Speakers (3.5mm)", AudioDeviceType.LineOut35mm);
        var cableIn = CreateDevice("cable-in", "CABLE Input (VB-Audio Point)", AudioDeviceType.VirtualAudio);
        var cableOut = CreateDevice("cable-out", "CABLE Output (VB-Audio Point)", AudioDeviceType.VirtualAudio);

        discovery.PlaybackDevices.AddRange([tv, ht, cableIn]);
        discovery.RecordingDevices.Add(cableOut);
        // Default playback set to CABLE Input
        discovery.DefaultPlayback = cableIn;

        var settings = new FakeSettingsService();
        settings.CurrentSettings.TvBranch.DeviceId = "tv-1";
        settings.CurrentSettings.HomeTheatreBranch.DeviceId = "ht-1";
        settings.CurrentSettings.PreferredPrimaryDeviceId = "tv-1";
        settings.CurrentSettings.PreferredSecondaryDeviceId = "ht-1";
        settings.CurrentSettings.PreferredCaptureDeviceId = "cable-out";

        using var service = new SystemAudioRoutingService(
            discovery,
            new FakeAudioMonitorService(),
            settings,
            new FakeDualAudioOutputService(),
            NullLogger<SystemAudioRoutingService>.Instance);

        var status = await service.ValidateRoutingAsync();

        Assert.Equal(RoutingValidationState.FullyConfiguredAndReady, status.ValidationState);
        Assert.True(status.IsDesktopAudioRoutedToVirtualCable);
        Assert.True(status.IsCaptureConfiguredCorrectly);
        Assert.True(status.IsReady);
        Assert.Contains("System Audio Routing: Ready", status.StatusTitle);
        Assert.True(status.HasBypassConditions); // Documented educational bypass items present
    }

    [Fact]
    public async Task DetectVirtualAudioCable_PairsCABLEInputAndOutputCorrectly()
    {
        var discovery = new FakeAudioDiscoveryService();
        discovery.PlaybackDevices.Add(CreateDevice("p-cable", "CABLE Input (VB-Audio Virtual Cable)", AudioDeviceType.VirtualAudio));
        discovery.RecordingDevices.Add(CreateDevice("r-cable", "CABLE Output (VB-Audio Virtual Cable)", AudioDeviceType.VirtualAudio));

        var settings = new FakeSettingsService();

        using var service = new SystemAudioRoutingService(
            discovery,
            new FakeAudioMonitorService(),
            settings,
            new FakeDualAudioOutputService(),
            NullLogger<SystemAudioRoutingService>.Instance);

        var pair = await service.DetectVirtualAudioCableAsync();

        Assert.NotNull(pair);
        Assert.True(pair.IsInstalled);
        Assert.Equal("p-cable", pair.PlaybackDevice?.Id);
        Assert.Equal("r-cable", pair.RecordingDevice?.Id);
        Assert.Equal("VB-Audio Virtual Cable", pair.DriverName);
    }

    [Fact]
    public void OutputDelayBuffer_Reset_PurgesStaleBacklog_AndPreventsDesynchronization()
    {
        var buffer = new OutputDelayBuffer(AudioOutputBranch.HomeTheatre35mm, sampleRate: 48000, channels: 2, maxDelaySeconds: 1.0f);
        
        // Write 0.5s of audio (24,000 frames = 48,000 samples)
        float[] writtenAudio = new float[48000];
        Array.Fill(writtenAudio, 0.75f);
        buffer.Write(writtenAudio);

        Assert.True(buffer.BufferOccupancyPercentage > 40f);

        // Disconnection / Reconnection occurs -> Reset must purge backlog
        buffer.Reset();

        Assert.Equal(0f, buffer.BufferOccupancyPercentage);
        Assert.Equal(0, buffer.UnderrunCount);
        Assert.Equal(0, buffer.OverrunCount);

        // Reading after reset outputs clean silence (no stale audio replay)
        float[] readBuffer = new float[960];
        int readCount = buffer.Read(readBuffer);
        Assert.Equal(960, readCount);
        Assert.All(readBuffer, sample => Assert.Equal(0f, sample));
    }

    [Fact]
    public void OutputDelayBuffer_Underflow_OutputsCleanSilence_WithoutMemoryFault()
    {
        var buffer = new OutputDelayBuffer(AudioOutputBranch.TvHdmi, sampleRate: 48000, channels: 2);
        
        // Buffer is empty: read should return silence and record underrun without throwing
        float[] readBuffer = new float[960];
        Array.Fill(readBuffer, 0.99f); // Fill with noise

        int readCount = buffer.Read(readBuffer);

        Assert.Equal(960, readCount);
        Assert.All(readBuffer, sample => Assert.Equal(0f, sample));
        Assert.True(buffer.UnderrunCount > 0);
    }

    [Fact]
    public void OutputDelayBuffer_DelayTrimming_DiscardsExcessLatencyImmediately()
    {
        var buffer = new OutputDelayBuffer(AudioOutputBranch.TvHdmi, sampleRate: 48000, channels: 2);
        
        // Configure 200 ms delay
        buffer.SetDelayMilliseconds(200f);
        
        // Write 300 ms of audio (14,400 frames = 28,800 samples)
        float[] data = new float[28800];
        Array.Fill(data, 0.5f);
        buffer.Write(data);

        // Reduce delay to 50 ms -> buffer should trim the excess 150 ms immediately
        buffer.SetDelayMilliseconds(50f);
        Assert.Equal(50f, buffer.TargetDelayMs);

        // Read audio -> Should immediately play without 150ms stale lag
        float[] readBuffer = new float[960];
        int readCount = buffer.Read(readBuffer);
        Assert.Equal(960, readCount);
        Assert.Equal(0.5f, readBuffer[0]);
    }

    private static AudioDeviceInfo CreateDevice(string id, string name, AudioDeviceType type)
    {
        return new AudioDeviceInfo
        {
            Id = id,
            Name = name,
            DeviceType = type,
            DeviceTypeDescription = type.ToString(),
            State = AudioDeviceState.Active
        };
    }

    #region Fakes and Stubs

    private sealed class FakeAudioDiscoveryService : IAudioDeviceDiscoveryService
    {
        public List<AudioDeviceInfo> PlaybackDevices { get; } = [];
        public List<AudioDeviceInfo> RecordingDevices { get; } = [];
        public AudioDeviceInfo? DefaultPlayback { get; set; }
        public AudioDeviceInfo? DefaultRecording { get; set; }

        public Task<IReadOnlyList<AudioDeviceInfo>> GetPlaybackDevicesAsync(bool includeInactive = true, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AudioDeviceInfo>>(PlaybackDevices);

        public Task<IReadOnlyList<AudioDeviceInfo>> GetRecordingDevicesAsync(bool includeInactive = true, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AudioDeviceInfo>>(RecordingDevices);

        public Task<AudioDeviceInfo?> GetDefaultPlaybackDeviceAsync(CancellationToken cancellationToken = default) => Task.FromResult(DefaultPlayback);

        public Task<AudioDeviceInfo?> GetDefaultRecordingDeviceAsync(CancellationToken cancellationToken = default) => Task.FromResult(DefaultRecording);

        public Task<AudioDeviceInfo?> GetDeviceByIdAsync(string deviceId, CancellationToken cancellationToken = default)
        {
            var dev = PlaybackDevices.FirstOrDefault(d => d.Id == deviceId) ?? RecordingDevices.FirstOrDefault(d => d.Id == deviceId);
            return Task.FromResult(dev);
        }
    }

    private sealed class FakeAudioMonitorService : IAudioDeviceMonitorService
    {
        public bool IsMonitoring => true;
        public event EventHandler<DeviceChangeEventArgs>? DeviceChanged;
        public void StartMonitoring() { }
        public void StopMonitoring() { }
        public void Dispose() { }
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings CurrentSettings { get; set; } = new();
        public string SettingsFilePath => "test_settings.json";
        public event EventHandler<AppSettings>? SettingsChanged;

        public Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default) => Task.FromResult(CurrentSettings);
        public Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            CurrentSettings = settings;
            SettingsChanged?.Invoke(this, settings);
            return Task.CompletedTask;
        }

        public Task ResetToDefaultsAsync()
        {
            CurrentSettings = new AppSettings();
            SettingsChanged?.Invoke(this, CurrentSettings);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDualAudioOutputService : IDualAudioOutputService
    {
        public IAudioOutputBranchService TvBranch { get; } = new FakeBranchService(AudioOutputBranch.TvHdmi);
        public IAudioOutputBranchService HomeTheatreBranch { get; } = new FakeBranchService(AudioOutputBranch.HomeTheatre35mm);
        public AudioEngineState State { get; set; } = AudioEngineState.Stopped;

        public event EventHandler<AudioEngineState>? StateChanged;
        public event EventHandler<string>? OutputError;

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

        public void FeedCapturedAudio(ReadOnlySpan<float> canonicalStereoSamples) { }
        public void FeedBranchAudio(AudioOutputBranch branch, ReadOnlySpan<float> canonicalStereoSamples) { }
        public void ApplySettings(AppSettings settings) { }
        public void Dispose() { }
    }

    private sealed class FakeBranchService : IAudioOutputBranchService
    {
        public AudioOutputBranch Branch { get; }
        public string? DeviceId { get; set; }
        public string DeviceName { get; set; } = "Fake Device";
        public string FormatDescription { get; set; } = "48kHz Stereo";
        public float Volume { get; set; } = 1.0f;
        public bool IsMuted { get; set; } = false;
        public bool IsEnabled { get; set; } = true;
        public float DelayMilliseconds { get; set; } = 0f;
        public AudioLevels CurrentLevels { get; set; } = new();
        public AudioSyncStatus SyncStatus { get; set; } = new();
        public AudioEngineState State { get; set; } = AudioEngineState.Stopped;

        public FakeBranchService(AudioOutputBranch branch) => Branch = branch;
        public void SetDevice(string? deviceId) => DeviceId = deviceId;
        public void ApplySettings(OutputBranchSettings settings) { }
    }

    #endregion
}
