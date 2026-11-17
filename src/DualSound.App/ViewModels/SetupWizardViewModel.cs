using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DualSound.Contracts.Enums;
using DualSound.Contracts.Models;
using DualSound.Core.Services;
using DualSound.Core.ViewModels;
using Microsoft.Extensions.Logging;

namespace DualSound.App.ViewModels;

/// <summary>
/// ViewModel guiding users through the 7-step First-Time Setup Wizard:
/// 1. Welcome & Virtual Cable Requirement
/// 2. Audio Source Detection (CABLE Input / Output Pairing)
/// 3. Windows Audio Routing (Setting CABLE Input as Default Playback)
/// 4. Physical Output Selection (TV HDMI vs Home Theatre 3.5mm)
/// 5. Permission & Capability Diagnostic
/// 6. Safe Audio Test Tone
/// 7. Completion & Launch
/// </summary>
public sealed partial class SetupWizardViewModel : ViewModelBase
{
    private readonly ISystemAudioRoutingService _routingService;
    private readonly IAudioDeviceDiscoveryService _discoveryService;
    private readonly ISettingsService _settingsService;
    private readonly IAudioEngineService _audioEngine;
    private readonly INavigationService _navigationService;
    private readonly IDispatcherService _dispatcherService;
    private readonly ILogger<SetupWizardViewModel> _logger;
    private readonly System.Threading.SemaphoreSlim _refreshLock = new(1, 1);

    [ObservableProperty]
    private int _currentStep = 1;

    [ObservableProperty]
    private string _stepTitle = "Step 1: Welcome to DualSound";

    // Step 1
    [ObservableProperty]
    private bool _isVirtualCableInstalled;

    [ObservableProperty]
    private string _virtualCableStatusText = "Scanning for virtual audio cable...";

    // Step 2
    [ObservableProperty]
    private AudioDeviceInfo? _detectedVirtualPlayback;

    [ObservableProperty]
    private AudioDeviceInfo? _detectedVirtualCapture;

    [ObservableProperty]
    private ObservableCollection<AudioDeviceInfo> _availableCaptureDevices = [];

    [ObservableProperty]
    private AudioDeviceInfo? _selectedCaptureDevice;

    // Step 3
    [ObservableProperty]
    private AudioDeviceInfo? _currentDefaultPlaybackDevice;

    [ObservableProperty]
    private bool _isDefaultPlaybackCorrect;

    [ObservableProperty]
    private string _routingStatusMessage = "Checking system playback endpoint...";

    // Step 4
    [ObservableProperty]
    private ObservableCollection<AudioDeviceInfo> _availablePlaybackDevices = [];

    [ObservableProperty]
    private AudioDeviceInfo? _selectedTvDevice;

    [ObservableProperty]
    private AudioDeviceInfo? _selectedHomeTheatreDevice;

    [ObservableProperty]
    private bool _areOutputsValid;

    [ObservableProperty]
    private string _outputsValidationMessage = string.Empty;

    // Step 5
    [ObservableProperty]
    private bool _isCaptureProbeSuccess;

    [ObservableProperty]
    private bool _isTvProbeSuccess;

    [ObservableProperty]
    private bool _isHomeTheatreProbeSuccess;

    [ObservableProperty]
    private string _readinessStatusMessage = "Validating audio pipeline capabilities...";

    // Step 6
    [ObservableProperty]
    private bool _isPlayingTone;

    [ObservableProperty]
    private string _activeToneTarget = "Ready to test";

    // Step 7
    [ObservableProperty]
    private bool _startEngineOnFinish = true;

    public SetupWizardViewModel(
        ISystemAudioRoutingService routingService,
        IAudioDeviceDiscoveryService discoveryService,
        ISettingsService settingsService,
        IAudioEngineService audioEngine,
        INavigationService navigationService,
        IDispatcherService dispatcherService,
        ILogger<SetupWizardViewModel> logger)
    {
        _routingService = routingService;
        _discoveryService = discoveryService;
        _settingsService = settingsService;
        _audioEngine = audioEngine;
        _navigationService = navigationService;
        _dispatcherService = dispatcherService;
        _logger = logger;

        Title = "Audio Setup Wizard";

        _routingService.RoutingStatusChanged += OnRoutingStatusChanged;

        _ = InitializeWizardAsync();
    }

    private async Task InitializeWizardAsync()
    {
        await RefreshDiagnosticsAsync();
    }

    [RelayCommand]
    public async Task RefreshDiagnosticsAsync()
    {
        await _refreshLock.WaitAsync();
        try
        {
            var status = await _routingService.ValidateRoutingAsync();
            var virtualPair = await _routingService.DetectVirtualAudioCableAsync();
            var playbacks = await _discoveryService.GetPlaybackDevicesAsync(includeInactive: false);
            var recordings = await _discoveryService.GetRecordingDevicesAsync(includeInactive: false);

            _dispatcherService.Invoke(() =>
            {
                // Step 1
                IsVirtualCableInstalled = virtualPair?.IsInstalled == true;
                VirtualCableStatusText = IsVirtualCableInstalled
                    ? $"Installed: {virtualPair!.DriverName}"
                    : "Not Detected. VB-Audio Virtual Cable is recommended.";

                // Step 2
                DetectedVirtualPlayback = virtualPair?.PlaybackDevice;
                DetectedVirtualCapture = virtualPair?.RecordingDevice;

                AvailableCaptureDevices.Clear();
                foreach (var r in recordings) AvailableCaptureDevices.Add(r);

                SelectedCaptureDevice = AvailableCaptureDevices.FirstOrDefault(d => d.Id == _settingsService.CurrentSettings.PreferredCaptureDeviceId)
                                     ?? virtualPair?.RecordingDevice
                                     ?? AvailableCaptureDevices.FirstOrDefault();

                // Step 3
                CurrentDefaultPlaybackDevice = status.WindowsDefaultPlaybackDevice;
                IsDefaultPlaybackCorrect = status.IsDesktopAudioRoutedToVirtualCable;
                RoutingStatusMessage = IsDefaultPlaybackCorrect
                    ? "Windows Default Playback is correctly routed through your virtual cable!"
                    : $"Windows is currently playing to '{CurrentDefaultPlaybackDevice?.Name ?? "None"}'. Desktop audio will bypass DualSound until set to '{DetectedVirtualPlayback?.Name ?? "CABLE Input"}'.";

                // Step 4
                AvailablePlaybackDevices.Clear();
                foreach (var p in playbacks) AvailablePlaybackDevices.Add(p);

                var settings = _settingsService.CurrentSettings;
                SelectedTvDevice = AvailablePlaybackDevices.FirstOrDefault(d => d.Id == settings.TvBranch?.DeviceId)
                                ?? AvailablePlaybackDevices.FirstOrDefault(d => d.DeviceType == AudioDeviceType.HdmiDisplayPort)
                                ?? AvailablePlaybackDevices.FirstOrDefault(d => d.Name?.Contains("TV", StringComparison.OrdinalIgnoreCase) == true)
                                ?? AvailablePlaybackDevices.FirstOrDefault();

                SelectedHomeTheatreDevice = AvailablePlaybackDevices.FirstOrDefault(d => d.Id == settings.HomeTheatreBranch?.DeviceId)
                                         ?? AvailablePlaybackDevices.FirstOrDefault(d => d.DeviceType is AudioDeviceType.LineOut35mm or AudioDeviceType.Headphones or AudioDeviceType.Speakers && d.Id != SelectedTvDevice?.Id)
                                         ?? AvailablePlaybackDevices.FirstOrDefault(d => d.Id != SelectedTvDevice?.Id);

                ValidateOutputs();

                // Step 5
                IsCaptureProbeSuccess = status.IsCaptureConfiguredCorrectly;
                IsTvProbeSuccess = SelectedTvDevice != null;
                IsHomeTheatreProbeSuccess = SelectedHomeTheatreDevice != null;
                ReadinessStatusMessage = (IsCaptureProbeSuccess && IsTvProbeSuccess && IsHomeTheatreProbeSuccess)
                    ? "All audio pipeline endpoints verified and ready for rendering."
                    : "Some audio endpoints need configuration.";
            });
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    partial void OnSelectedTvDeviceChanged(AudioDeviceInfo? value) => ValidateOutputs();
    partial void OnSelectedHomeTheatreDeviceChanged(AudioDeviceInfo? value) => ValidateOutputs();

    private void ValidateOutputs()
    {
        if (SelectedTvDevice == null || SelectedHomeTheatreDevice == null)
        {
            AreOutputsValid = false;
            OutputsValidationMessage = "Both TV and Home Theatre output devices must be selected.";
            return;
        }

        if (string.Equals(SelectedTvDevice.Id, SelectedHomeTheatreDevice.Id, StringComparison.OrdinalIgnoreCase))
        {
            AreOutputsValid = false;
            OutputsValidationMessage = "TV and Home Theatre cannot be set to the exact same audio device.";
            return;
        }

        AreOutputsValid = true;
        OutputsValidationMessage = "Valid dual output configuration.";
    }

    [RelayCommand]
    public void OpenWindowsSoundSettings()
    {
        _routingService.OpenWindowsSoundSettings();
    }

    [RelayCommand]
    public async Task NextStepAsync()
    {
        if (CurrentStep < 7)
        {
            CurrentStep++;
            UpdateStepTitle();

            // Save progressive configuration at key steps
            if (CurrentStep == 4 && SelectedCaptureDevice != null)
            {
                var s = _settingsService.CurrentSettings;
                s.PreferredCaptureDeviceId = SelectedCaptureDevice.Id;
                await _settingsService.SaveSettingsAsync(s);
            }
            else if (CurrentStep == 5 && SelectedTvDevice != null && SelectedHomeTheatreDevice != null)
            {
                var s = _settingsService.CurrentSettings;
                if (s.TvBranch == null) s.TvBranch = new() { Branch = AudioOutputBranch.TvHdmi };
                if (s.HomeTheatreBranch == null) s.HomeTheatreBranch = new() { Branch = AudioOutputBranch.HomeTheatre35mm };

                s.TvBranch.DeviceId = SelectedTvDevice.Id;
                s.TvBranch.DeviceName = SelectedTvDevice.Name;
                s.PreferredPrimaryDeviceId = SelectedTvDevice.Id;

                s.HomeTheatreBranch.DeviceId = SelectedHomeTheatreDevice.Id;
                s.HomeTheatreBranch.DeviceName = SelectedHomeTheatreDevice.Name;
                s.PreferredSecondaryDeviceId = SelectedHomeTheatreDevice.Id;

                await _settingsService.SaveSettingsAsync(s);
            }

            await RefreshDiagnosticsAsync();
        }
    }

    [RelayCommand]
    public void PreviousStep()
    {
        if (CurrentStep > 1)
        {
            CurrentStep--;
            UpdateStepTitle();
        }
    }

    private void UpdateStepTitle()
    {
        StepTitle = CurrentStep switch
        {
            1 => "Step 1: Welcome & Virtual Audio Cable",
            2 => "Step 2: Audio Source Discovery",
            3 => "Step 3: Windows Default Playback Routing",
            4 => "Step 4: Physical Output Assignment",
            5 => "Step 5: Capability & Readiness Probe",
            6 => "Step 6: Safe Acoustic Test Tone",
            7 => "Step 7: Complete Setup & Launch",
            _ => "Audio Setup Wizard"
        };
    }

    [RelayCommand]
    public async Task TestTvToneAsync()
    {
        IsPlayingTone = true;
        ActiveToneTarget = "Playing safe test tone to TV (HDMI)...";
        await _routingService.PlayTestToneAsync(AudioOutputBranch.TvHdmi, 3.0f, 0.20f);
        IsPlayingTone = false;
        ActiveToneTarget = "Test complete";
    }

    [RelayCommand]
    public async Task TestHomeTheatreToneAsync()
    {
        IsPlayingTone = true;
        ActiveToneTarget = "Playing safe test tone to Home Theatre (3.5 mm)...";
        await _routingService.PlayTestToneAsync(AudioOutputBranch.HomeTheatre35mm, 3.0f, 0.20f);
        IsPlayingTone = false;
        ActiveToneTarget = "Test complete";
    }

    [RelayCommand]
    public async Task TestBothTonesAsync()
    {
        IsPlayingTone = true;
        ActiveToneTarget = "Playing safe test tone to TV and Home Theatre simultaneously...";
        await _routingService.PlayTestToneAsync(null, 3.0f, 0.20f);
        IsPlayingTone = false;
        ActiveToneTarget = "Test complete";
    }

    [RelayCommand]
    public void StopTestTone()
    {
        _routingService.StopTestTone();
        IsPlayingTone = false;
        ActiveToneTarget = "Test stopped";
    }

    [RelayCommand]
    public async Task CompleteSetupAsync()
    {
        StopTestTone();

        // Mark wizard complete in settings
        var settings = _settingsService.CurrentSettings;
        settings.IsSetupWizardCompleted = true;

        if (SelectedCaptureDevice != null) settings.PreferredCaptureDeviceId = SelectedCaptureDevice.Id;
        if (SelectedTvDevice != null)
        {
            if (settings.TvBranch == null) settings.TvBranch = new() { Branch = AudioOutputBranch.TvHdmi };
            settings.TvBranch.DeviceId = SelectedTvDevice.Id;
            settings.TvBranch.DeviceName = SelectedTvDevice.Name;
            settings.PreferredPrimaryDeviceId = SelectedTvDevice.Id;
        }
        if (SelectedHomeTheatreDevice != null)
        {
            if (settings.HomeTheatreBranch == null) settings.HomeTheatreBranch = new() { Branch = AudioOutputBranch.HomeTheatre35mm };
            settings.HomeTheatreBranch.DeviceId = SelectedHomeTheatreDevice.Id;
            settings.HomeTheatreBranch.DeviceName = SelectedHomeTheatreDevice.Name;
            settings.PreferredSecondaryDeviceId = SelectedHomeTheatreDevice.Id;
        }

        await _settingsService.SaveSettingsAsync(settings);

        if (StartEngineOnFinish && _audioEngine.State != AudioEngineState.Running)
        {
            try
            {
                await _audioEngine.StartAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not auto-start engine on setup wizard completion");
            }
        }

        // Navigate to Dashboard
        _navigationService.NavigateTo<DashboardViewModel>();
    }

    [RelayCommand]
    public void ExitWizard()
    {
        StopTestTone();
        _navigationService.NavigateTo<DashboardViewModel>();
    }

    private void OnRoutingStatusChanged(object? sender, SystemAudioRoutingStatus e)
    {
        _dispatcherService.Invoke(() =>
        {
            CurrentDefaultPlaybackDevice = e.WindowsDefaultPlaybackDevice;
            IsDefaultPlaybackCorrect = e.IsDesktopAudioRoutedToVirtualCable;
            IsCaptureProbeSuccess = e.IsCaptureConfiguredCorrectly;
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _routingService.RoutingStatusChanged -= OnRoutingStatusChanged;
            StopTestTone();
            _refreshLock.Dispose();
        }
        base.Dispose(disposing);
    }
}
