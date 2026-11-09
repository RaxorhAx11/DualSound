using System;
using System.Threading.Tasks;
using DualSound.App.Services;
using DualSound.App.ViewModels;
using DualSound.Audio.Capture;
using DualSound.Audio.Engine;
using DualSound.Audio.Output;
using DualSound.Audio.Presets;
using DualSound.Audio.Sync;
using DualSound.Core.Services;
using DualSound.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DualSound.Tests;

public class ViewModelLifecycleTests
{
    private readonly IServiceProvider _serviceProvider;

    public ViewModelLifecycleTests()
    {
        var services = new ServiceCollection();

        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IDispatcherService, TestDispatcherService>();
        services.AddSingleton<ISettingsService>(sp =>
            new JsonSettingsService(sp.GetRequiredService<ILogger<JsonSettingsService>>()));
        services.AddSingleton<IAudioDeviceDiscoveryService, WindowsAudioDeviceDiscoveryService>();
        services.AddSingleton<IAudioDeviceMonitorService, WindowsAudioDeviceMonitorService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IAudioCapture, WasapiCaptureService>();
        services.AddSingleton<IAudioSyncManager, AudioSyncManager>();
        services.AddSingleton<IDualAudioOutputService, DualAudioOutputService>();
        services.AddSingleton<IAudioPresetService, AudioPresetService>();
        services.AddSingleton<IAudioEngineService, AudioEngineService>();

        services.AddSingleton<MainViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<AudioDevicesViewModel>();
        services.AddTransient<AudioRoutingViewModel>();
        services.AddTransient<EqualizerViewModel>();
        services.AddTransient<SynchronizationViewModel>();
        services.AddTransient<SettingsViewModel>();

        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public void CanInstantiateMainViewModelAndNavigateAllPages()
    {
        var navService = _serviceProvider.GetRequiredService<INavigationService>();
        var mainVm = _serviceProvider.GetRequiredService<MainViewModel>();

        Assert.NotNull(mainVm);
        Assert.Equal(6, mainVm.NavigationItems.Count);

        // Verify initial navigation to Dashboard
        Assert.IsType<DashboardViewModel>(mainVm.CurrentView);

        // Navigate to Audio Devices
        navService.NavigateTo<AudioDevicesViewModel>();
        Assert.IsType<AudioDevicesViewModel>(mainVm.CurrentView);
        Assert.Equal("Audio Devices", mainVm.CurrentView.Title);

        // Navigate to Audio Routing
        navService.NavigateTo<AudioRoutingViewModel>();
        Assert.IsType<AudioRoutingViewModel>(mainVm.CurrentView);
        var routingVm = (AudioRoutingViewModel)mainVm.CurrentView;
        Assert.Equal("Audio Routing", routingVm.Title);

        // Navigate to Equalizer
        navService.NavigateTo<EqualizerViewModel>();
        Assert.IsType<EqualizerViewModel>(mainVm.CurrentView);
        var eqVm = (EqualizerViewModel)mainVm.CurrentView;
        Assert.Equal("Equalizer", eqVm.Title);
        Assert.True(eqVm.IsEqEnabled);

        // Navigate to Synchronization
        navService.NavigateTo<SynchronizationViewModel>();
        Assert.IsType<SynchronizationViewModel>(mainVm.CurrentView);
        var syncVm = (SynchronizationViewModel)mainVm.CurrentView;
        Assert.Equal("Synchronization", syncVm.Title);

        // Navigate to Settings
        navService.NavigateTo<SettingsViewModel>();
        Assert.IsType<SettingsViewModel>(mainVm.CurrentView);
        var settingsVm = (SettingsViewModel)mainVm.CurrentView;
        Assert.False(string.IsNullOrWhiteSpace(settingsVm.SettingsFilePath));
    }

    [Fact]
    public async Task AudioDevicesViewModel_PopulatesFilteredDevicesAndCounts()
    {
        var devicesVm = _serviceProvider.GetRequiredService<AudioDevicesViewModel>();
        await devicesVm.RefreshDevicesAsync();

        Assert.True(devicesVm.TotalDeviceCount > 0, "Expected at least one device on Windows system");
        Assert.True(devicesVm.ActiveDeviceCount > 0, "Expected at least one active device on Windows system");
        Assert.NotEmpty(devicesVm.FilteredDevices);
        Assert.NotNull(devicesVm.SelectedDevice);

        // Test search filter
        devicesVm.SearchQuery = "NONEXISTENT_XYZ_DEVICE_NAME";
        Assert.Empty(devicesVm.FilteredDevices);

        devicesVm.SearchQuery = string.Empty;
        Assert.NotEmpty(devicesVm.FilteredDevices);
    }

    [Fact]
    public async Task DashboardViewModel_LoadsAndPopulatesCards()
    {
        var dashboardVm = _serviceProvider.GetRequiredService<DashboardViewModel>();
        await dashboardVm.LoadDashboardAsync();

        Assert.NotNull(dashboardVm.EngineStatus);
        Assert.Contains("Offline", dashboardVm.EngineStatus);
        Assert.NotNull(dashboardVm.DefaultDevice);
        Assert.NotEmpty(dashboardVm.AllPlaybackDevices);
    }

    [Fact]
    public async Task DashboardViewModel_AnalogAvailability_DisplaysUnavailableWhenActive()
    {
        var dashboardVm = _serviceProvider.GetRequiredService<DashboardViewModel>();
        await dashboardVm.LoadDashboardAsync();

        if (dashboardVm.SelectedAnalogDevice != null && dashboardVm.SelectedAnalogDevice.IsActive)
        {
            // Must strictly display "Connection status unavailable" per prompt instructions
            Assert.Equal("Connection status unavailable", dashboardVm.AnalogConnectionAvailability);
        }
    }

    [Fact]
    public async Task DashboardViewModel_DeviceSelection_UpdatesSettings()
    {
        var dashboardVm = _serviceProvider.GetRequiredService<DashboardViewModel>();
        var settingsService = _serviceProvider.GetRequiredService<ISettingsService>();
        await dashboardVm.LoadDashboardAsync();

        if (dashboardVm.AvailableHdmiDevices.Count > 0)
        {
            var testDev = dashboardVm.AvailableHdmiDevices[0];
            dashboardVm.SelectedHdmiDevice = testDev;
            Assert.Equal(testDev.Id, settingsService.CurrentSettings.PreferredPrimaryDeviceId);
        }
    }

    [Fact]
    public async Task AudioDevicesViewModel_FiltersWorkAgainstDeviceData()
    {
        var devicesVm = _serviceProvider.GetRequiredService<AudioDevicesViewModel>();
        await devicesVm.RefreshDevicesAsync();

        devicesVm.SelectedFilter = "Active";
        Assert.All(devicesVm.FilteredDevices, d => Assert.True(d.IsActive));

        devicesVm.SelectedFilter = "All";
        Assert.Equal(devicesVm.TotalDeviceCount, devicesVm.FilteredDevices.Count);
    }

    [Fact]
    public async Task AudioRoutingViewModel_PopulatesRecordingDevicesAndHandlesCommands()
    {
        var routingVm = _serviceProvider.GetRequiredService<AudioRoutingViewModel>();
        await routingVm.RefreshRecordingDevicesAsync();

        Assert.NotNull(routingVm.AvailableRecordingDevices);
        Assert.NotEmpty(routingVm.AvailableRecordingDevices);
        Assert.NotNull(routingVm.SelectedRecordingDevice);
        Assert.Equal("Ready", routingVm.CaptureStatusText);
        Assert.True(routingVm.CanStartCapture);
        Assert.False(routingVm.CanStopCapture);
    }

    private sealed class TestDispatcherService : IDispatcherService
    {
        public void Invoke(Action action) => action();
        public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
        public Task<T> InvokeAsync<T>(Func<T> func) => Task.FromResult(func());
    }
}

