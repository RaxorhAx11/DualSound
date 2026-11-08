using DualSound.Core.ViewModels;

namespace DualSound.App.ViewModels;

/// <summary>
/// Placeholder ViewModel for the upcoming Audio Routing engine.
/// Clearly displays "Coming Soon" without non-functional controls.
/// </summary>
public sealed class AudioRoutingViewModel : ViewModelBase
{
    public string Status => "Coming Soon";

    public string FeatureName => "Audio Routing & Multi-Device Distribution";

    public string MilestoneDescription =>
        "This feature is coming soon in Milestone 02.\n\n" +
        "DualSound will enable application audio capture and simultaneous real-time distribution " +
        "to both your TV (via HDMI) and your Home Theatre (via 3.5 mm analog jack) with independent volume controls.";

    public AudioRoutingViewModel()
    {
        Title = "Audio Routing";
    }
}
