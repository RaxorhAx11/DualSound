using DualSound.Core.ViewModels;

namespace DualSound.App.ViewModels;

/// <summary>
/// Placeholder ViewModel for Equalizer processing.
/// Clearly displays "Coming Soon" without non-functional controls.
/// </summary>
public sealed class EqualizerViewModel : ViewModelBase
{
    public string Status => "Coming Soon";

    public string FeatureName => "Multi-Channel Parametric Equalizer";

    public string MilestoneDescription =>
        "This feature is coming soon in Milestone 03.\n\n" +
        "DualSound will feature dedicated multi-band parametric and graphic EQ processing per output endpoint, " +
        "allowing customized acoustics for TV speakers and Home Theatre subwoofers independently.";

    public EqualizerViewModel()
    {
        Title = "Equalizer";
    }
}
