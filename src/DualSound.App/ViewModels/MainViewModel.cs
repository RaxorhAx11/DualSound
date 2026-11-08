using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DualSound.Contracts.Events;
using DualSound.Core.Services;
using DualSound.Core.ViewModels;
using Microsoft.Extensions.Logging;

namespace DualSound.App.ViewModels;

public sealed record NavigationItem(string Title, string IconGeometryKey, Type ViewModelType);

/// <summary>
/// Top-level shell ViewModel managing sidebar navigation, active page hosting,
/// and application status bar monitoring.
/// </summary>
public sealed partial class MainViewModel : ViewModelBase
{
    private readonly INavigationService _navigationService;
    private readonly IAudioDeviceMonitorService _monitorService;
    private readonly IAudioDeviceDiscoveryService _discoveryService;
    private readonly IDispatcherService _dispatcherService;
    private readonly ILogger<MainViewModel> _logger;

    [ObservableProperty]
    private ViewModelBase? _currentView;

    [ObservableProperty]
    private NavigationItem? _selectedNavigationItem;

    [ObservableProperty]
    private string _defaultPlaybackDeviceName = "Scanning...";

    [ObservableProperty]
    private int _activeDevicesCount;

    [ObservableProperty]
    private string? _deviceEventNotification;

    public ObservableCollection<NavigationItem> NavigationItems { get; } = [];

    public MainViewModel(
        INavigationService navigationService,
        IAudioDeviceMonitorService monitorService,
        IAudioDeviceDiscoveryService discoveryService,
        IDispatcherService dispatcherService,
        ILogger<MainViewModel> logger)
    {
        _navigationService = navigationService;
        _monitorService = monitorService;
        _discoveryService = discoveryService;
        _dispatcherService = dispatcherService;
        _logger = logger;

        Title = "DualSound";

        InitializeNavigation();

        _navigationService.CurrentViewModelChanged += (s, e) =>
        {
            CurrentView = _navigationService.CurrentViewModel;
            SelectedNavigationItem = NavigationItems.FirstOrDefault(n => n.ViewModelType == CurrentView?.GetType());
        };

        _monitorService.DeviceChanged += OnAudioDeviceChanged;

        // Navigate to Dashboard initially
        _navigationService.NavigateTo<DashboardViewModel>();

        // Query default device name for status bar
        _ = UpdateStatusAsync();
    }

    private void InitializeNavigation()
    {
        NavigationItems.Add(new NavigationItem("Dashboard", "IconDashboard", typeof(DashboardViewModel)));
        NavigationItems.Add(new NavigationItem("Audio Devices", "IconDevices", typeof(AudioDevicesViewModel)));
        NavigationItems.Add(new NavigationItem("Audio Routing", "IconRouting", typeof(AudioRoutingViewModel)));
        NavigationItems.Add(new NavigationItem("Equalizer", "IconEqualizer", typeof(EqualizerViewModel)));
        NavigationItems.Add(new NavigationItem("Synchronization", "IconSync", typeof(SynchronizationViewModel)));
        NavigationItems.Add(new NavigationItem("Settings", "IconSettings", typeof(SettingsViewModel)));
    }

    [RelayCommand]
    public void Navigate(NavigationItem? item)
    {
        if (item != null && item.ViewModelType != CurrentView?.GetType())
        {
            _navigationService.NavigateTo(item.ViewModelType);
        }
    }

    [RelayCommand]
    private void DismissNotification()
    {
        DeviceEventNotification = null;
    }

    private async Task UpdateStatusAsync()
    {
        try
        {
            var defaultDev = await _discoveryService.GetDefaultPlaybackDeviceAsync();
            var activeDevs = await _discoveryService.GetPlaybackDevicesAsync(includeInactive: false);

            _dispatcherService.Invoke(() =>
            {
                DefaultPlaybackDeviceName = defaultDev?.Name ?? "No Default Device";
                ActiveDevicesCount = activeDevs.Count;
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update status bar device info");
        }
    }

    private void OnAudioDeviceChanged(object? sender, DeviceChangeEventArgs e)
    {
        _dispatcherService.Invoke(() =>
        {
            DeviceEventNotification = e.ChangeType switch
            {
                DeviceChangeType.DeviceAdded => "Audio hardware connected",
                DeviceChangeType.DeviceRemoved => "Audio hardware disconnected",
                DeviceChangeType.DeviceStateChanged => $"Audio device state changed ({e.NewState})",
                DeviceChangeType.DefaultDeviceChanged => "Default Windows playback device changed",
                _ => "Audio endpoint updated"
            };

            _ = UpdateStatusAsync();
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _monitorService.DeviceChanged -= OnAudioDeviceChanged;
        }
        base.Dispose(disposing);
    }
}
