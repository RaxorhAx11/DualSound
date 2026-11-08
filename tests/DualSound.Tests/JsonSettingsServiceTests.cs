using System;
using System.IO;
using System.Threading.Tasks;
using DualSound.Contracts.Models;
using DualSound.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DualSound.Tests;

public class JsonSettingsServiceTests
{
    [Fact]
    public async Task SaveAndLoad_PersistsPreferencesAccurately()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"dualsound_test_{Guid.NewGuid():N}.json");
        try
        {
            var logger = NullLogger<JsonSettingsService>.Instance;
            var service = new JsonSettingsService(logger, tempFile);

            var original = new AppSettings
            {
                Theme = "Dark",
                AutoRefreshDevices = false,
                RefreshIntervalSeconds = 12,
                ShowDisconnectedDevices = false,
                ShowDisabledDevices = true,
                PreferredPrimaryDeviceId = "{0.0.0.00000000}.{TEST-HDMI}",
                PreferredSecondaryDeviceId = "{0.0.0.00000000}.{TEST-35MM}"
            };

            await service.SaveSettingsAsync(original);

            var loaded = await service.LoadSettingsAsync();

            Assert.Equal(original.Theme, loaded.Theme);
            Assert.Equal(original.AutoRefreshDevices, loaded.AutoRefreshDevices);
            Assert.Equal(original.RefreshIntervalSeconds, loaded.RefreshIntervalSeconds);
            Assert.Equal(original.ShowDisconnectedDevices, loaded.ShowDisconnectedDevices);
            Assert.Equal(original.ShowDisabledDevices, loaded.ShowDisabledDevices);
            Assert.Equal(original.PreferredPrimaryDeviceId, loaded.PreferredPrimaryDeviceId);
            Assert.Equal(original.PreferredSecondaryDeviceId, loaded.PreferredSecondaryDeviceId);
            Assert.NotNull(loaded.LastSavedAtUtc);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task Load_WhenFileCorrupt_RecoversToDefaultSettings()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"dualsound_corrupt_{Guid.NewGuid():N}.json");
        try
        {
            var logger = NullLogger<JsonSettingsService>.Instance;
            var service = new JsonSettingsService(logger, tempFile);

            // Write corrupt file
            await File.WriteAllTextAsync(tempFile, "{ broken json corrupt content }");

            var loaded = await service.LoadSettingsAsync();

            Assert.NotNull(loaded);
            Assert.Equal("Dark", loaded.Theme);
            Assert.True(loaded.AutoRefreshDevices);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
