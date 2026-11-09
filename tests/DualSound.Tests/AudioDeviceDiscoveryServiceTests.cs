using System.Linq;
using System.Threading.Tasks;
using DualSound.Contracts.Enums;
using DualSound.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DualSound.Tests;

public class AudioDeviceDiscoveryServiceTests
{
    [Fact]
    public async Task GetPlaybackDevicesAsync_ReturnsActualWindowsEndpoints()
    {
        var logger = NullLogger<WindowsAudioDeviceDiscoveryService>.Instance;
        var service = new WindowsAudioDeviceDiscoveryService(logger);

        var devices = await service.GetPlaybackDevicesAsync(includeInactive: true);

        Assert.NotNull(devices);
        Assert.NotEmpty(devices);

        // Every device must have a non-empty ID and Name
        foreach (var device in devices)
        {
            Assert.False(string.IsNullOrWhiteSpace(device.Id), "Device ID must not be blank");
            Assert.False(string.IsNullOrWhiteSpace(device.Name), "Device Name must not be blank");
            Assert.True(device.State != 0, "Device State must be valid");
            Assert.NotNull(device.DeviceTypeDescription);
            Assert.NotEqual(string.Empty, device.DeviceTypeDescription);
        }

        // Check if at least one active device exists on the system
        var activeDevices = devices.Where(d => d.State == AudioDeviceState.Active).ToList();
        Assert.NotEmpty(activeDevices);

        // Check default playback device
        var defaultDevice = await service.GetDefaultPlaybackDeviceAsync();
        if (defaultDevice != null)
        {
            Assert.True(defaultDevice.IsDefaultPlayback);
            Assert.True(defaultDevice.IsActive);
        }
    }

    [Fact]
    public async Task GetDeviceByIdAsync_WhenIdNotFound_ReturnsNullWithoutThrowing()
    {
        var logger = NullLogger<WindowsAudioDeviceDiscoveryService>.Instance;
        var service = new WindowsAudioDeviceDiscoveryService(logger);

        var nonExistent = await service.GetDeviceByIdAsync("{0.0.0.00000000}.{00000000-0000-0000-0000-000000000000}");
        Assert.Null(nonExistent);
    }
}

