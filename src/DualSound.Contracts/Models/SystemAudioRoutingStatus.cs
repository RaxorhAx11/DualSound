using System.Collections.Generic;
using DualSound.Contracts.Enums;

namespace DualSound.Contracts.Models;

/// <summary>
/// Comprehensive diagnostic snapshot of Windows system audio routing,
/// endpoint readiness, capture sources, physical destinations, and potential bypasses.
/// </summary>
public sealed class SystemAudioRoutingStatus
{
    public RoutingValidationState ValidationState { get; set; } = RoutingValidationState.Uninitialized;

    public AudioDeviceInfo? WindowsDefaultPlaybackDevice { get; set; }
    public AudioDeviceInfo? VirtualPlaybackDevice { get; set; }
    public AudioDeviceInfo? VirtualRecordingDevice { get; set; }
    public AudioDeviceInfo? TvOutputDevice { get; set; }
    public AudioDeviceInfo? HomeTheatreOutputDevice { get; set; }

    public bool IsDesktopAudioRoutedToVirtualCable { get; set; }
    public bool IsCaptureConfiguredCorrectly { get; set; }
    public bool IsTvConfiguredCorrectly { get; set; }
    public bool IsHomeTheatreConfiguredCorrectly { get; set; }
    public bool IsEngineRunning { get; set; }
    public bool IsSystemAudioDetected { get; set; }

    public string StatusTitle { get; set; } = "Checking Audio Routing...";
    public string StatusSummary { get; set; } = "Evaluating Windows audio endpoint configuration.";
    public string RecommendedActionText { get; set; } = string.Empty;
    public string ActionCommandOrUrl { get; set; } = string.Empty;
    public bool CanOpenSoundSettings => !string.IsNullOrEmpty(ActionCommandOrUrl);

    public List<string> KnownBypasses { get; set; } = [];
    public bool HasBypassConditions => KnownBypasses.Count > 0;
    public string? BypassDetails => KnownBypasses.Count > 0 ? string.Join("; ", KnownBypasses) : null;

    public VirtualAudioCablePair? DetectedVirtualCable { get; set; }

    public bool IsReady => ValidationState == RoutingValidationState.FullyConfiguredAndReady;
}
