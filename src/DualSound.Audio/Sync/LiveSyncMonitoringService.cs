using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DualSound.Contracts.Enums;
using DualSound.Contracts.Models;
using DualSound.Core.Services;
using Microsoft.Extensions.Logging;

namespace DualSound.Audio.Sync;

/// <summary>
/// Service managing continuous real-time clock drift monitoring and smooth micro-corrections
/// between TV HDMI and Home Theatre 3.5mm hardware outputs.
/// </summary>
public sealed class LiveSyncMonitoringService : ILiveSyncMonitoringService
{
    private readonly IAudioSyncManager _syncManager;
    private readonly IAudioEngineService _audioEngine;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<LiveSyncMonitoringService> _logger;
    private readonly object _lock = new();

    private Timer? _monitorTimer;
    private bool _isEnabled;
    private bool _isDisposed;

    private readonly Queue<float> _driftHistory = new(16);
    private long _totalCorrections;
    private DateTime _lastCorrectionTime = DateTime.MinValue;
    private long _previousTvUnderruns;
    private long _previousHtUnderruns;
    private LiveSyncStatus _currentStatus = LiveSyncStatus.Disabled;

    public bool IsEnabled
    {
        get { lock (_lock) return _isEnabled; }
        set
        {
            lock (_lock)
            {
                if (_isEnabled == value) return;
                _isEnabled = value;
                _settingsService.CurrentSettings.IsLiveSyncMonitoringEnabled = value;
                _ = _settingsService.SaveSettingsAsync(_settingsService.CurrentSettings);

                if (_isEnabled)
                {
                    ResetInternal();
                    _currentStatus = new LiveSyncStatus(
                        IsEnabled: true,
                        IsActive: _audioEngine.State == AudioEngineState.Running,
                        EstimatedDriftMs: 0f,
                        DriftSamples: 0,
                        TotalCorrectionsApplied: _totalCorrections,
                        StatusDescription: _audioEngine.State == AudioEngineState.Running ? "Active - Monitoring Clock Drift" : "Monitoring Inactive (Audio Engine Stopped)",
                        LimitationNotice: _audioEngine.State == AudioEngineState.Running ? null : "Live drift monitoring requires active audio playback to measure hardware clock differences.");
                }
                else
                {
                    _currentStatus = LiveSyncStatus.Disabled;
                }
            }

            OnStatusUpdated();
        }
    }

    public LiveSyncStatus CurrentStatus
    {
        get { lock (_lock) return _currentStatus; }
    }

    public event EventHandler<LiveSyncStatus>? StatusChanged;

    public LiveSyncMonitoringService(
        IAudioSyncManager syncManager,
        IAudioEngineService audioEngine,
        ISettingsService settingsService,
        ILogger<LiveSyncMonitoringService> logger)
    {
        _syncManager = syncManager;
        _audioEngine = audioEngine;
        _settingsService = settingsService;
        _logger = logger;

        _isEnabled = _settingsService.CurrentSettings.IsLiveSyncMonitoringEnabled;
        if (_isEnabled)
        {
            _currentStatus = new LiveSyncStatus(
                IsEnabled: true,
                IsActive: false,
                EstimatedDriftMs: 0f,
                DriftSamples: 0,
                TotalCorrectionsApplied: 0,
                StatusDescription: "Monitoring Inactive (Audio Engine Stopped)",
                LimitationNotice: "Live drift monitoring requires active audio playback to measure hardware clock differences.");
        }

        // Telemetry evaluation timer at 4 Hz (250 ms)
        _monitorTimer = new Timer(OnMonitorTimerTick, null, 250, 250);
    }

    public void Start()
    {
        IsEnabled = true;
    }

    public void Stop()
    {
        IsEnabled = false;
    }

    public void Reset()
    {
        lock (_lock)
        {
            ResetInternal();
        }
        OnStatusUpdated();
    }

    private void ResetInternal()
    {
        _driftHistory.Clear();
        _previousTvUnderruns = 0;
        _previousHtUnderruns = 0;
        _lastCorrectionTime = DateTime.MinValue;
    }

    private void OnMonitorTimerTick(object? state)
    {
        if (_isDisposed) return;

        lock (_lock)
        {
            if (!_isEnabled) return;

            // Diagnostic Check 1: Audio Engine State
            if (_audioEngine.State != AudioEngineState.Running)
            {
                _driftHistory.Clear();
                _currentStatus = LiveSyncStatus.EngineStopped;
                OnStatusUpdated();
                return;
            }

            var tvStatus = _syncManager.GetStatus(AudioOutputBranch.TvHdmi);
            var htStatus = _syncManager.GetStatus(AudioOutputBranch.HomeTheatre35mm);

            // Diagnostic Check 2: Buffer Starvation / Underrun Reliability
            bool hasNewUnderruns = (tvStatus.UnderrunCount > _previousTvUnderruns) ||
                                  (htStatus.UnderrunCount > _previousHtUnderruns);
            _previousTvUnderruns = tvStatus.UnderrunCount;
            _previousHtUnderruns = htStatus.UnderrunCount;

            if (hasNewUnderruns || tvStatus.BufferOccupancyPercentage < 2f || htStatus.BufferOccupancyPercentage < 2f)
            {
                _driftHistory.Clear();
                _currentStatus = new LiveSyncStatus(
                    IsEnabled: true,
                    IsActive: true,
                    EstimatedDriftMs: 0f,
                    DriftSamples: 0,
                    TotalCorrectionsApplied: _totalCorrections,
                    StatusDescription: "Measurement Unreliable (Buffer Starvation / Underruns)",
                    LimitationNotice: "Hardware buffer underruns detected on output device. Timing drift measurement suspended to avoid unstable corrections.",
                    IsReliable: false);
                OnStatusUpdated();
                return;
            }

            // Diagnostic Check 3: Calculate Differential Clock Drift
            // Differential drift in samples: TvDrift - HtDrift
            long differentialDriftSamples = tvStatus.DriftSamples - htStatus.DriftSamples;
            float differentialDriftMs = (float)differentialDriftSamples / 48000f * 1000f;

            _driftHistory.Enqueue(differentialDriftMs);
            if (_driftHistory.Count > 12) _driftHistory.Dequeue();

            // Need at least 6 samples (~1.5s) to establish moving average
            if (_driftHistory.Count < 6)
            {
                _currentStatus = new LiveSyncStatus(
                    IsEnabled: true,
                    IsActive: true,
                    EstimatedDriftMs: differentialDriftMs,
                    DriftSamples: differentialDriftSamples,
                    TotalCorrectionsApplied: _totalCorrections,
                    StatusDescription: "Analyzing Clock Drift (Accumulating Telemetry)...",
                    LimitationNotice: null,
                    IsReliable: true);
                OnStatusUpdated();
                return;
            }

            float avgDriftMs = _driftHistory.Average();
            float variance = _driftHistory.Average(d => (d - avgDriftMs) * (d - avgDriftMs));
            float stdDev = MathF.Sqrt(variance);

            // High jitter / variance indicates transient OS scheduling delay rather than true crystal drift
            if (stdDev > 1.8f)
            {
                _currentStatus = new LiveSyncStatus(
                    IsEnabled: true,
                    IsActive: true,
                    EstimatedDriftMs: avgDriftMs,
                    DriftSamples: differentialDriftSamples,
                    TotalCorrectionsApplied: _totalCorrections,
                    StatusDescription: "Analyzing Clock Drift (High Timing Jitter)",
                    LimitationNotice: "Transient scheduling jitter observed. Awaiting consistent crystal drift trend.",
                    IsReliable: true);
                OnStatusUpdated();
                return;
            }

            float toleranceMs = _settingsService.CurrentSettings.LiveSyncToleranceMs;
            if (toleranceMs < 0.5f) toleranceMs = 2.0f;

            // Check if drift exceeds tolerance
            if (MathF.Abs(avgDriftMs) > toleranceMs)
            {
                // Cooldown: enforce at least 5 seconds between micro-corrections to avoid hunting/oscillations
                if ((DateTime.UtcNow - _lastCorrectionTime).TotalSeconds >= 5.0)
                {
                    // Apply small, smooth correction (0.5 ms step)
                    const float microStepMs = 0.5f;

                    if (avgDriftMs > 0)
                    {
                        // TV buffer is accumulating samples / running slower than HT:
                        // Delay HT branch slightly to realign
                        _syncManager.AdjustDelaySmoothly(AudioOutputBranch.HomeTheatre35mm, microStepMs);
                    }
                    else
                    {
                        // HT buffer is accumulating samples / running slower than TV:
                        // Delay TV branch slightly to realign
                        _syncManager.AdjustDelaySmoothly(AudioOutputBranch.TvHdmi, microStepMs);
                    }

                    _totalCorrections++;
                    _lastCorrectionTime = DateTime.UtcNow;
                    _driftHistory.Clear(); // Reset window after applying correction

                    _logger.LogInformation("Live Sync Monitoring: Applied smooth {Step} ms drift correction (Drift: {Drift:F1} ms, Total Corrections: {Count})",
                        microStepMs, avgDriftMs, _totalCorrections);

                    _currentStatus = new LiveSyncStatus(
                        IsEnabled: true,
                        IsActive: true,
                        EstimatedDriftMs: avgDriftMs,
                        DriftSamples: differentialDriftSamples,
                        TotalCorrectionsApplied: _totalCorrections,
                        StatusDescription: $"Active - Applied Smooth Correction ({avgDriftMs:+0.0;-0.0} ms)",
                        LimitationNotice: null,
                        IsReliable: true);
                }
                else
                {
                    _currentStatus = new LiveSyncStatus(
                        IsEnabled: true,
                        IsActive: true,
                        EstimatedDriftMs: avgDriftMs,
                        DriftSamples: differentialDriftSamples,
                        TotalCorrectionsApplied: _totalCorrections,
                        StatusDescription: $"Active - Clock Drift Detected ({avgDriftMs:+0.0;-0.0} ms) [Cooldown]",
                        LimitationNotice: null,
                        IsReliable: true);
                }
            }
            else
            {
                _currentStatus = new LiveSyncStatus(
                    IsEnabled: true,
                    IsActive: true,
                    EstimatedDriftMs: avgDriftMs,
                    DriftSamples: differentialDriftSamples,
                    TotalCorrectionsApplied: _totalCorrections,
                    StatusDescription: "Active - Outputs Synchronized (Drift < 2 ms)",
                    LimitationNotice: null,
                    IsReliable: true);
            }

            OnStatusUpdated();
        }
    }

    private void OnStatusUpdated()
    {
        StatusChanged?.Invoke(this, _currentStatus);
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            _monitorTimer?.Dispose();
        }
    }
}

