using System;
using System.Threading.Tasks;
using DualSound.App.Services;
using DualSound.App.ViewModels;
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
        Assert.Equal("Audio Endpoints", mainVm.CurrentView.Title);

        // Navigate to Audio Routing (Coming Soon)
        navService.NavigateTo<AudioRoutingViewModel>();
        Assert.IsType<AudioRoutingViewModel>(mainVm.CurrentView);
        var routingVm = (AudioRoutingViewModel)mainVm.CurrentView;
        Assert.Contains("Coming Soon", routingVm.MilestoneDescription, StringComparison.OrdinalIgnoreCase);

        // Navigate to Equalizer (Coming Soon)
        navService.NavigateTo<EqualizerViewModel>();
        Assert.IsType<EqualizerViewModel>(mainVm.CurrentView);
        var eqVm = (EqualizerViewModel)mainVm.CurrentView;
        Assert.Contains("Coming Soon", eqVm.MilestoneDescription, StringComparison.OrdinalIgnoreCase);

        // Navigate to Synchronization (Coming Soon)
        navService.NavigateTo<SynchronizationViewModel>();
        Assert.IsType<SynchronizationViewModel>(mainVm.CurrentView);
        var syncVm = (SynchronizationViewModel)mainVm.CurrentView;
        Assert.Contains("Coming Soon", syncVm.MilestoneDescription, StringComparison.OrdinalIgnoreCase);

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

    private sealed class TestDispatcherService : IDispatcherService
    {
        public void Invoke(Action action) => action();
        public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
        public Task<T> InvokeAsync<T>(Func<T> func) => Task.FromResult(func());
    }
}
