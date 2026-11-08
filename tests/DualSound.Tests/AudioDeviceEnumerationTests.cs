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

            for (int i = 0; i < device.Properties.Count; i++)
            {
                var key = device.Properties.Get(i);
                try
                {
                    var prop = device.Properties[key];
                    var val = prop.Value;
                    if (val != null)
                    {
                        var keyStr = $"{key.formatId}:{key.propertyId}";
                        _output.WriteLine($"  Prop [{keyStr}] = {val}");
                    }
                }
                catch { }
            }
            _output.WriteLine(new string('-', 50));
        }
    }
}
