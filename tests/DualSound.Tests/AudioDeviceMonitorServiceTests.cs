using System;
using DualSound.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DualSound.Tests;

public class AudioDeviceMonitorServiceTests
{
    [Fact]
    public void StartAndStopMonitoring_ManagesLifecycleProperly()
    {
        var logger = NullLogger<WindowsAudioDeviceMonitorService>.Instance;
        using var monitor = new WindowsAudioDeviceMonitorService(logger);

        Assert.False(monitor.IsMonitoring);

        monitor.StartMonitoring();
        Assert.True(monitor.IsMonitoring);

        // Calling start again when already running should be idempotent
        monitor.StartMonitoring();
        Assert.True(monitor.IsMonitoring);

        monitor.StopMonitoring();
        Assert.False(monitor.IsMonitoring);

        // Calling stop again when stopped should be idempotent
        monitor.StopMonitoring();
        Assert.False(monitor.IsMonitoring);
    }
}
