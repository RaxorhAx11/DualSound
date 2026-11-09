namespace DualSound.Contracts.Enums;

/// <summary>
/// Digital biquad filter transfer function categories.
/// </summary>
public enum AudioFilterType
{
    Peaking = 0,
    LowPass = 1,
    HighPass = 2,
    LowShelf = 3,
    HighShelf = 4,
    BandPass = 5
}

