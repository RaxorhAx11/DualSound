using DualSound.Core.ViewModels;

namespace DualSound.App.ViewModels;

/// <summary>
/// Placeholder ViewModel for Audio Synchronization.
/// Clearly displays "Coming Soon" without non-functional controls.
/// </summary>
public sealed class SynchronizationViewModel : ViewModelBase
{
    public string Status => "Coming Soon";

    public string FeatureName => "Endpoint Latency & Audio Synchronization";

    public string MilestoneDescription =>
        "This feature is coming soon in Milestone 04.\n\n" +
        "DualSound will provide sub-millisecond audio sync adjustment between HDMI digital display outputs " +
        "and analog 3.5 mm lines to eliminate echo and acoustic phasing across rooms and speakers.";

    public SynchronizationViewModel()
    {
        Title = "Synchronization";
    }
}
