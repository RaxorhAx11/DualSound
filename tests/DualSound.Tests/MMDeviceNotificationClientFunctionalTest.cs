using System;
using NAudio.CoreAudioApi;
using Xunit;

namespace DualSound.Tests;

public class MMDeviceNotificationClientFunctionalTest
{
    [Fact]
    public void CanCreateAndDisposeNotificationClient()
    {
        using var enumerator = new MMDeviceEnumerator();
        using var client = enumerator.CreateNotificationClient(useSynchronizationContext: false);
        Assert.NotNull(client);

        bool eventHooked = false;
        void OnDeviceAdded(object? sender, DeviceNotificationEventArgs e)
        {
            eventHooked = true;
        }

        client.DeviceAdded += OnDeviceAdded;
        client.DeviceAdded -= OnDeviceAdded;
        Assert.False(eventHooked);
    }
}

