using System;
using DualSound.Contracts.Models;

namespace DualSound.Audio.Capture;

/// <summary>
/// Real-time audio level measurement engine calculating true peak, RMS, and signal presence.
/// Implements standard audio meter ballistics with instantaneous attack and smooth decay.
/// Thread-safe and allocation-free.
/// </summary>
public sealed class AudioLevelMeter
{
    private const float SignalThresholdLinear = 0.001f; // -60 dBFS
    private const float DecayFactor = 0.82f;            // Smooth meter release falloff

    private readonly object _syncLock = new();

    private float _leftPeak;
    private float _rightPeak;
    private float _leftRms;
    private float _rightRms;
    private bool _hasSignal;

    /// <summary>
    /// Gets the current audio levels snapshot.
    /// </summary>
    public AudioLevels CurrentLevels
    {
        get
        {
            lock (_syncLock)
            {
                return new AudioLevels
                {
                    LeftPeak = _leftPeak,
                    RightPeak = _rightPeak,
                    LeftRms = _leftRms,
                    RightRms = _rightRms,
                    HasSignal = _hasSignal
                };
            }
        }
    }

    /// <summary>
    /// Analyzes an incoming buffer of interleaved stereo float samples (L0, R0, L1, R1...)
    /// and updates peak and RMS measurements with ballistics smoothing.
    /// </summary>
    /// <param name="stereoSamples">Interleaved stereo float samples.</param>
    public void ProcessSamples(ReadOnlySpan<float> stereoSamples)
    {
        if (stereoSamples.IsEmpty)
        {
            DecayLevels();
            return;
        }

        int frameCount = stereoSamples.Length / 2;
        if (frameCount == 0)
        {
            DecayLevels();
            return;
        }

        float instantLeftPeak = 0.0f;
        float instantRightPeak = 0.0f;
        double sumSqL = 0.0;
        double sumSqR = 0.0;

        for (int i = 0; i < frameCount; i++)
        {
            float l = Math.Abs(stereoSamples[i * 2]);
            float r = Math.Abs(stereoSamples[i * 2 + 1]);

            if (l > instantLeftPeak) instantLeftPeak = l;
            if (r > instantRightPeak) instantRightPeak = r;

            sumSqL += l * l;
            sumSqR += r * r;
        }

        float instantLeftRms = (float)Math.Sqrt(sumSqL / frameCount);
        float instantRightRms = (float)Math.Sqrt(sumSqR / frameCount);

        lock (_syncLock)
        {
            // Instant attack, exponential decay ballistics
            _leftPeak = instantLeftPeak >= _leftPeak ? instantLeftPeak : Math.Max(instantLeftPeak, _leftPeak * DecayFactor);
            _rightPeak = instantRightPeak >= _rightPeak ? instantRightPeak : Math.Max(instantRightPeak, _rightPeak * DecayFactor);

            _leftRms = instantLeftRms >= _leftRms ? instantLeftRms : Math.Max(instantLeftRms, _leftRms * DecayFactor);
            _rightRms = instantRightRms >= _rightRms ? instantRightRms : Math.Max(instantRightRms, _rightRms * DecayFactor);

            // Signal presence detection
            _hasSignal = instantLeftPeak > SignalThresholdLinear || instantRightPeak > SignalThresholdLinear;
        }
    }

    /// <summary>
    /// Smoothly decays meters when no new audio is arriving.
    /// </summary>
    public void DecayLevels()
    {
        lock (_syncLock)
        {
            _leftPeak = _leftPeak * DecayFactor;
            _rightPeak = _rightPeak * DecayFactor;
            _leftRms = _leftRms * DecayFactor;
            _rightRms = _rightRms * DecayFactor;

            if (_leftPeak < 0.0001f) _leftPeak = 0f;
            if (_rightPeak < 0.0001f) _rightPeak = 0f;
            if (_leftRms < 0.0001f) _leftRms = 0f;
            if (_rightRms < 0.0001f) _rightRms = 0f;

            _hasSignal = false;
        }
    }

    /// <summary>
    /// Instantly clears all meter levels to 0.
    /// </summary>
    public void Reset()
    {
        lock (_syncLock)
        {
            _leftPeak = 0f;
            _rightPeak = 0f;
            _leftRms = 0f;
            _rightRms = 0f;
            _hasSignal = false;
        }
    }
}

