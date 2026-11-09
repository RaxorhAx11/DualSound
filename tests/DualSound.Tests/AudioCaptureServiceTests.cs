using System;
using System.Threading;
using System.Threading.Tasks;
using DualSound.Audio.Capture;
using DualSound.Contracts.Enums;
using DualSound.Contracts.Models;
using DualSound.Core.Services;
using DualSound.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DualSound.Tests;

public class AudioCaptureServiceTests
{
    private readonly ISettingsService _settingsService;
    private readonly IAudioDeviceDiscoveryService _discoveryService;
    private readonly IAudioDeviceMonitorService _monitorService;

    public AudioCaptureServiceTests()
    {
        _settingsService = new JsonSettingsService(NullLogger<JsonSettingsService>.Instance);
        _discoveryService = new WindowsAudioDeviceDiscoveryService(NullLogger<WindowsAudioDeviceDiscoveryService>.Instance);
        _monitorService = new WindowsAudioDeviceMonitorService(NullLogger<WindowsAudioDeviceMonitorService>.Instance);
    }

    [Fact]
    public void Service_InitialState_IsStoppedWithEmptyLevels()
    {
        using var service = new WasapiCaptureService(
            _discoveryService,
            _monitorService,
            _settingsService,
            NullLogger<WasapiCaptureService>.Instance);

        Assert.Equal(AudioCaptureState.Stopped, service.State);
        Assert.Equal(0, service.TotalFramesCaptured);
        Assert.Equal(0f, service.BufferOccupancyPercentage);
        Assert.Equal(0f, service.CurrentLevels.LeftPeak);
        Assert.Equal(0f, service.CurrentLevels.RightPeak);
        Assert.False(service.CurrentLevels.HasSignal);
    }

    [Fact]
    public void SetDevice_UpdatesSelectedDeviceAndPersists()
    {
        using var service = new WasapiCaptureService(
            _discoveryService,
            _monitorService,
            _settingsService,
            NullLogger<WasapiCaptureService>.Instance);

        var testDevice = new AudioDeviceInfo
        {
            Id = "{0.0.1.00000000}.{TEST-CAPTURE-DEVICE-GUID}",
            Name = "CABLE Output (VB-Audio Virtual Cable)",
            State = AudioDeviceState.Active,
            DataFlow = AudioDataFlow.Capture
        };

        service.SetDevice(testDevice);

        Assert.NotNull(service.SelectedDevice);
        Assert.Equal(testDevice.Id, service.SelectedDevice.Id);
        Assert.Equal(testDevice.Id, _settingsService.CurrentSettings.PreferredCaptureDeviceId);
    }

    [Fact]
    public async Task StopAsync_WhenAlreadyStopped_IsSafeAndNoOp()
    {
        using var service = new WasapiCaptureService(
            _discoveryService,
            _monitorService,
            _settingsService,
            NullLogger<WasapiCaptureService>.Instance);

        // Stopping an already stopped service should complete cleanly without throwing
        await service.StopAsync();
        Assert.Equal(AudioCaptureState.Stopped, service.State);
    }

    [Fact]
    public void Options_ConfiguresCanonicalTargetParameters()
    {
        var options = new AudioCaptureOptions
        {
            TargetSampleRate = 48000,
            TargetChannels = 2,
            BufferDurationMilliseconds = 50,
            RingBufferDurationSeconds = 1.0f
        };

        using var service = new WasapiCaptureService(
            _discoveryService,
            _monitorService,
            _settingsService,
            NullLogger<WasapiCaptureService>.Instance,
            options);

        Assert.Equal(48000, service.Options.TargetSampleRate);
        Assert.Equal(2, service.Options.TargetChannels);
        Assert.Equal(50, service.Options.BufferDurationMilliseconds);
        Assert.Equal(96000, service.Buffer.Capacity);
    }

    /// <summary>
    /// Functional hardware test: Tests real WASAPI recording endpoint capture on live Windows hardware.
    /// Captures live audio packets for 500 ms, verifies packet arrival and state transitions.
    /// </summary>
    [Fact]
    public async Task LiveHardwareCapture_WhenActiveDeviceAvailable_StreamsAudioPackets()
    {
        var recordingDevices = await _discoveryService.GetRecordingDevicesAsync(includeInactive: false);
        if (recordingDevices.Count == 0)
        {
            // Skip hardware capture if machine has zero active recording endpoints (e.g. headless CI)
            return;
        }

        using var service = new WasapiCaptureService(
            _discoveryService,
            _monitorService,
            _settingsService,
            NullLogger<WasapiCaptureService>.Instance);

        int packetsReceived = 0;
        service.AudioPacketReceived += (s, e) =>
        {
            Interlocked.Increment(ref packetsReceived);
        };

        var target = recordingDevices[0];
        service.SetDevice(target);

        await service.StartAsync(target.Id);
        Assert.Equal(AudioCaptureState.Capturing, service.State);

        // Capture for 400 milliseconds
        await Task.Delay(400);

        Assert.True(service.TotalFramesCaptured > 0, "Expected non-zero captured frames on active recording endpoint");
        Assert.True(packetsReceived > 0, "Expected AudioPacketReceived events to fire");

        await service.StopAsync();
        Assert.Equal(AudioCaptureState.Stopped, service.State);
    }
}

