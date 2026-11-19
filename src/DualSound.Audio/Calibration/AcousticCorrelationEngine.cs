using System;
using System.Collections.Generic;
using System.Linq;
using DualSound.Contracts.Models;

namespace DualSound.Audio.Calibration;

/// <summary>
/// Matched filter cross-correlation and time-of-flight estimation result for a single recorded chirp.
/// </summary>
public sealed record CorrelationDetectionResult(
    int PeakSampleIndex,
    float PeakTimeMs,
    float PeakValue,
    float NoiseFloor,
    float PeakToNoiseRatio,
    float Confidence,
    bool IsAcceptable,
    string? FailureReason = null);

/// <summary>
/// High-precision acoustic signal cross-correlation, time-of-arrival (TOA) detector,
/// and multi-pass alignment calculation engine.
/// </summary>
public static class AcousticCorrelationEngine
{
    public const float MinAcceptablePeakToNoiseRatio = 3.5f;
    public const float HighConfidencePeakToNoiseRatio = 6.0f;
    public const float MaxAmbientNoiseFloorDb = -22.0f; // -22 dBFS or louder is too noisy for reliable acoustic calibration
    public const float MinSignalPeakLevelDb = -50.0f;    // Quieter than -50 dBFS means mic is muted or signal undetected

    /// <summary>
    /// Computes acoustic time of arrival using hierarchical matched-filter cross-correlation.
    /// Stage 1 performs a decimated coarse search; Stage 2 refines at full resolution.
    /// </summary>
    public static CorrelationDetectionResult DetectArrival(
        ReadOnlySpan<float> recordedSignal,
        ReadOnlySpan<float> referenceChirp,
        int sampleRate = 48000,
        int maxSearchSamples = -1)
    {
        if (recordedSignal.Length < referenceChirp.Length || referenceChirp.IsEmpty)
        {
            return new CorrelationDetectionResult(0, 0f, 0f, 0f, 0f, 0f, false, "Recorded audio buffer shorter than reference signal.");
        }

        // Measure signal amplitude to ensure microphone pick-up was active
        float maxAbs = 0f;
        for (int i = 0; i < recordedSignal.Length; i++)
        {
            float abs = MathF.Abs(recordedSignal[i]);
            if (abs > maxAbs) maxAbs = abs;
        }

        float signalPeakDb = maxAbs > 1e-6f ? 20.0f * MathF.Log10(maxAbs) : -100.0f;
        if (signalPeakDb < MinSignalPeakLevelDb)
        {
            return new CorrelationDetectionResult(
                0, 0f, 0f, 0f, 0f, 0f, false,
                $"Microphone signal too faint ({signalPeakDb:F1} dBFS). Check microphone volume or position.");
        }

        int searchLimit = maxSearchSamples > 0 
            ? Math.Min(maxSearchSamples, recordedSignal.Length - referenceChirp.Length)
            : recordedSignal.Length - referenceChirp.Length;

        // Stage 1: Coarse search decimated by factor of 4 for speed
        const int decimate = 4;
        int coarseSearchLimit = searchLimit / decimate;
        int coarseRefLen = referenceChirp.Length / decimate;

        float bestCoarseCorr = float.MinValue;
        int bestCoarseIdx = 0;

        for (int k = 0; k < coarseSearchLimit; k++)
        {
            int signalStart = k * decimate;
            float sum = 0f;

            for (int m = 0; m < coarseRefLen; m++)
            {
                sum += referenceChirp[m * decimate] * recordedSignal[signalStart + (m * decimate)];
            }

            if (sum > bestCoarseCorr)
            {
                bestCoarseCorr = sum;
                bestCoarseIdx = signalStart;
            }
        }

        // Stage 2: Fine search at full 48 kHz in +/- 2 ms (+/- 96 samples) window around coarse peak
        int fineWindow = (int)(0.003f * sampleRate); // +/- 3 ms
        int fineStart = Math.Max(0, bestCoarseIdx - fineWindow);
        int fineEnd = Math.Min(searchLimit, bestCoarseIdx + fineWindow);

        float bestFineCorr = float.MinValue;
        int bestFineIdx = fineStart;

        for (int k = fineStart; k <= fineEnd; k++)
        {
            float sum = 0f;
            for (int m = 0; m < referenceChirp.Length; m++)
            {
                sum += referenceChirp[m] * recordedSignal[k + m];
            }

            if (sum > bestFineCorr)
            {
                bestFineCorr = sum;
                bestFineIdx = k;
            }
        }

        // Noise floor calculation: sample correlation outside the peak region (+/- 150 samples)
        int peakExclusionWindow = (int)(0.005f * sampleRate);
        float noiseSumSq = 0f;
        int noiseCount = 0;

        int step = Math.Max(1, searchLimit / 150); // 150 sample probes across the buffer
        for (int k = 0; k < searchLimit; k += step)
        {
            if (Math.Abs(k - bestFineIdx) < peakExclusionWindow) continue;

            float sum = 0f;
            for (int m = 0; m < referenceChirp.Length; m++)
            {
                sum += referenceChirp[m] * recordedSignal[k + m];
            }

            noiseSumSq += sum * sum;
            noiseCount++;
        }

        float noiseFloor = noiseCount > 0 ? MathF.Sqrt(noiseSumSq / noiseCount) : 0.0001f;
        float pnr = noiseFloor > 1e-6f ? Math.Max(0f, bestFineCorr / noiseFloor) : 0f;

        // Normalized correlation coefficient calculation
        float refEnergy = 0f;
        for (int m = 0; m < referenceChirp.Length; m++)
        {
            refEnergy += referenceChirp[m] * referenceChirp[m];
        }

        float recEnergy = 0f;
        for (int m = 0; m < referenceChirp.Length; m++)
        {
            float s = recordedSignal[bestFineIdx + m];
            recEnergy += s * s;
        }

        float normCorr = (refEnergy > 1e-6f && recEnergy > 1e-6f)
            ? Math.Clamp(bestFineCorr / MathF.Sqrt(refEnergy * recEnergy), -1.0f, 1.0f)
            : 0f;

        // Confidence scoring combining normalized correlation and peak-to-noise ratio
        bool isAcceptable = normCorr >= 0.30f && pnr >= MinAcceptablePeakToNoiseRatio;
        float confidence = isAcceptable
            ? Math.Clamp(normCorr * (pnr / (pnr + 1.5f)) * 1.25f, 0.60f, 0.98f)
            : Math.Clamp(Math.Max(0f, normCorr) * 0.8f, 0f, 0.50f);

        string? failure = isAcceptable ? null : $"Low correlation confidence (normalized: {normCorr * 100:F0}%, PNR: {pnr:F1}x). Possible room reflection or background noise.";

        float arrivalTimeMs = (float)bestFineIdx / sampleRate * 1000f;

        return new CorrelationDetectionResult(
            PeakSampleIndex: bestFineIdx,
            PeakTimeMs: arrivalTimeMs,
            PeakValue: bestFineCorr,
            NoiseFloor: noiseFloor,
            PeakToNoiseRatio: pnr,
            Confidence: confidence,
            IsAcceptable: isAcceptable,
            FailureReason: failure);
    }

    /// <summary>
    /// Measures ambient RMS noise floor in dBFS during a quiet period.
    /// </summary>
    public static float MeasureAmbientNoiseFloorDb(ReadOnlySpan<float> quietBuffer)
    {
        if (quietBuffer.IsEmpty) return -60f;

        float sumSq = 0f;
        for (int i = 0; i < quietBuffer.Length; i++)
        {
            sumSq += quietBuffer[i] * quietBuffer[i];
        }

        float rms = MathF.Sqrt(sumSq / quietBuffer.Length);
        if (rms < 1e-6f) return -100f;
        return 20.0f * MathF.Log10(rms);
    }

    /// <summary>
    /// Analyzes multiple measurement passes, filters outliers, and calculates robust delay recommendation.
    /// </summary>
    public static AcousticCalibrationResult CalculateAlignment(
        IReadOnlyList<CalibrationPassMeasurement> passes,
        float ambientNoiseDb = -60f)
    {
        if (passes == null || passes.Count == 0)
        {
            return AcousticCalibrationResult.Failed("No calibration passes recorded.");
        }

        var validPasses = passes.Where(p => p.IsAccepted).ToList();
        if (validPasses.Count == 0)
        {
            var firstFailure = passes.FirstOrDefault(p => !string.IsNullOrEmpty(p.RejectionReason))?.RejectionReason;
            return AcousticCalibrationResult.Failed(
                firstFailure ?? "Measurement confidence too low. Microphone could not detect clear acoustic signals.",
                "Ensure speakers are turned on, microphone is positioned at listening height, and background noise is minimal.");
        }

        // Outlier rejection if we have 3 or more passes: discard passes with delta deviating > 4 ms from median
        if (validPasses.Count >= 3)
        {
            var sortedDeltas = validPasses.Select(p => p.DifferenceMs).OrderBy(d => d).ToList();
            float medianDelta = sortedDeltas[sortedDeltas.Count / 2];

            validPasses = validPasses.Where(p => MathF.Abs(p.DifferenceMs - medianDelta) <= 4.0f).ToList();
            if (validPasses.Count == 0)
            {
                return AcousticCalibrationResult.Failed(
                    "High measurement variance between calibration passes.",
                    "Room reflections, flutter echoes, or moving microphone caused conflicting measurements. Keep microphone still and retry.");
            }
        }

        // Robust average of accepted passes
        float avgTvArrival = validPasses.Average(p => p.TvArrivalMs);
        float avgHtArrival = validPasses.Average(p => p.HomeTheatreArrivalMs);
        float avgDiff = validPasses.Average(p => p.DifferenceMs);

        // Standard deviation check
        float variance = validPasses.Average(p => MathF.Pow(p.DifferenceMs - avgDiff, 2));
        float stdDev = MathF.Sqrt(variance);

        // Calculate combined confidence
        float avgPnr = validPasses.Average(p => (p.TvPeakToNoiseRatio + p.HomeTheatrePeakToNoiseRatio) * 0.5f);
        float confidence = Math.Clamp((avgPnr - MinAcceptablePeakToNoiseRatio) / (HighConfidencePeakToNoiseRatio - MinAcceptablePeakToNoiseRatio), 0f, 1.0f);
        if (avgPnr >= HighConfidencePeakToNoiseRatio)
        {
            confidence = Math.Clamp(0.85f + (avgPnr - HighConfidencePeakToNoiseRatio) * 0.02f, 0.85f, 0.98f);
        }

        // Penalize confidence if standard deviation across passes is elevated
        if (stdDev > 2.0f)
        {
            confidence = Math.Max(0.1f, confidence - 0.25f);
        }

        bool isConfidenceAcceptable = confidence >= 0.60f && stdDev <= 3.5f;

        if (!isConfidenceAcceptable)
        {
            return new AcousticCalibrationResult
            {
                Success = false,
                IsConfidenceAcceptable = false,
                ConfidenceScore = confidence,
                MeasuredTvArrivalMs = avgTvArrival,
                MeasuredHomeTheatreArrivalMs = avgHtArrival,
                ArrivalDifferenceMs = avgDiff,
                AmbientNoiseFloorDb = ambientNoiseDb,
                Passes = passes.ToList(),
                Message = $"Measurement confidence ({confidence * 100:F0}%) below minimum required threshold (60%).",
                AcousticWarning = "Acoustic reflections from walls or ambient room noise interfered with measurement. Previous working delay settings have been preserved."
            };
        }

        // Calculate delay to apply to the faster output:
        // avgDiff = TvArrival - HtArrival
        // If avgDiff > 0: TV arrives later -> Home Theatre is faster -> delay Home Theatre by avgDiff
        // If avgDiff < 0: Home Theatre arrives later -> TV is faster -> delay TV by |avgDiff|
        // If |avgDiff| <= 0.5 ms: essentially simultaneous phase alignment -> 0 ms delay for both
        float recommendedTvDelay = 0f;
        float recommendedHtDelay = 0f;

        if (avgDiff > 0.5f)
        {
            recommendedHtDelay = Math.Clamp(MathF.Round(avgDiff), 0f, 500f);
            recommendedTvDelay = 0f;
        }
        else if (avgDiff < -0.5f)
        {
            recommendedTvDelay = Math.Clamp(MathF.Round(MathF.Abs(avgDiff)), 0f, 500f);
            recommendedHtDelay = 0f;
        }

        string alignmentSummary;
        if (MathF.Abs(avgDiff) <= 0.5f)
        {
            alignmentSummary = "Acoustic arrival times are virtually identical (< 0.5 ms difference). Both outputs are already synchronized.";
        }
        else if (avgDiff > 0)
        {
            alignmentSummary = $"TV audio arrives {avgDiff:F1} ms later than Home Theatre (likely HDMI video processing delay). Delaying Home Theatre by {recommendedHtDelay:F0} ms aligns both outputs.";
        }
        else
        {
            alignmentSummary = $"Home Theatre audio arrives {MathF.Abs(avgDiff):F1} ms later than TV. Delaying TV by {recommendedTvDelay:F0} ms aligns both outputs.";
        }

        return new AcousticCalibrationResult
        {
            Success = true,
            IsConfidenceAcceptable = true,
            ConfidenceScore = confidence,
            MeasuredTvArrivalMs = avgTvArrival,
            MeasuredHomeTheatreArrivalMs = avgHtArrival,
            ArrivalDifferenceMs = avgDiff,
            AmbientNoiseFloorDb = ambientNoiseDb,
            RecommendedTvDelayMs = recommendedTvDelay,
            RecommendedHomeTheatreDelayMs = recommendedHtDelay,
            Message = alignmentSummary,
            AcousticWarning = "Note: Room reflections and physical microphone position directly influence acoustic timing.",
            Passes = passes.ToList(),
            TimestampUtc = DateTime.UtcNow
        };
    }
}
