using System;
using NAudio.CoreAudioApi;
using Xunit;
using Xunit.Abstractions;

namespace DualSound.Tests;

public class AudioDeviceEnumerationTests
{
    private readonly ITestOutputHelper _output;

    public AudioDeviceEnumerationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void CanEnumerateActiveRenderDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.All);
        _output.WriteLine($"Total Render Devices Found: {devices.Count}");

        foreach (var device in devices)
        {
            _output.WriteLine($"Name: {device.FriendlyName}");
            _output.WriteLine($"ID: {device.ID}");
            _output.WriteLine($"State: {device.State}");
            _output.WriteLine(new string('-', 50));
        }
    }

    [Fact]
    public void CanEnumerateCaptureDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.All);
        _output.WriteLine($"Total Capture Devices Found: {devices.Count}");

        foreach (var device in devices)
        {
            _output.WriteLine($"Name: {device.FriendlyName}");
            _output.WriteLine($"ID: {device.ID}");
            _output.WriteLine($"State: {device.State}");
            _output.WriteLine(new string('-', 50));
        }
    }
}
