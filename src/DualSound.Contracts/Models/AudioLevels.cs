using System;

namespace DualSound.Contracts.Models;

/// <summary>
/// Real-time audio signal measurements representing Left and Right channel levels.
/// </summary>
public readonly record struct AudioLevels
{
    /// <summary>
    /// Left channel linear RMS level [0.0, 1.0].
    /// </summary>
    public float LeftRms { get; init; }

    /// <summary>
    /// Right channel linear RMS level [0.0, 1.0].
    /// </summary>
    public float RightRms { get; init; }

    /// <summary>
    /// Left channel linear peak level [0.0, 1.0].
    /// </summary>
    public float LeftPeak { get; init; }

    /// <summary>
    /// Right channel linear peak level [0.0, 1.0].
    /// </summary>
    public float RightPeak { get; init; }

    /// <summary>
    /// Master (maximum of Left and Right) linear peak level [0.0, 1.0].
    /// </summary>
    public float MasterPeak => Math.Max(LeftPeak, RightPeak);

    /// <summary>
    /// Left channel level in decibels relative to full scale (-60 dBFS to 0 dBFS).
    /// </summary>
    public float LeftPeakDb => LinearToDb(LeftPeak);

    /// <summary>
    /// Right channel level in decibels relative to full scale (-60 dBFS to 0 dBFS).
    /// </summary>
    public float RightPeakDb => LinearToDb(RightPeak);

    /// <summary>
    /// Master peak level in decibels relative to full scale (-60 dBFS to 0 dBFS).
    /// </summary>
    public float MasterPeakDb => LinearToDb(MasterPeak);

    /// <summary>
    /// True when active audio signal is detected above the -60 dBFS noise threshold.
    /// </summary>
    public bool HasSignal { get; init; }

    public static AudioLevels Empty => new()
    {
        LeftRms = 0f,
        RightRms = 0f,
        LeftPeak = 0f,
        RightPeak = 0f,
        HasSignal = false
    };

    private static float LinearToDb(float linear)
    {
        if (linear <= 0.0001f)
        {
            return -60f;
        }

        var db = 20.0f * (float)Math.Log10(linear);
        return Math.Clamp(db, -60.0f, 0.0f);
    }
}

